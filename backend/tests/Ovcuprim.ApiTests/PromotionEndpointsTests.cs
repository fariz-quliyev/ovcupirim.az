using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Payments;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// The promotion-purchase surface end to end: server-controlled pricing, ownership scoping,
/// signature verification, the authoritative status re-check, idempotency, expiry, and refund
/// reversal — all against a deterministic, mocked gateway. No real payment provider is ever called.
/// </summary>
public class PromotionEndpointsTests
{
    private const string SellerPhone = "+994501234567";
    private const string OtherSellerPhone = "+994551234567";
    private const string AdminPhone = "+994701234567";
    private const string CategorySlug = "bel-cantasi";
    private const string RegionSlug = "baki";

    private static async Task<ApiFactory> SeededFactoryAsync()
    {
        var factory = new ApiFactory();
        await factory.SeedTaxonomyAsync();
        return factory;
    }

    private static async Task<HttpClient> SignedInAsync(ApiFactory factory, string phone, UserRole role = UserRole.User)
    {
        var client = factory.CreateApiClient();

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(phone, "Test İstifadəçi"));
        var registration = await client.PostAsJsonAsync("/api/v1/auth/verify",
            new VerifyOtpRequest(phone, factory.Sms.LastCodeFor(phone), OtpPurpose.Registration));

        if (role == UserRole.User)
        {
            var registered = (await registration.Content.ReadFromJsonAsync<AuthResponse>())!;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.AccessToken);

            return client;
        }

        await factory.SetRoleAsync(phone, role);

        // Sign in again so the promoted role is inside a freshly minted token — through whichever
        // door that role uses, since an Admin account is outside the SMS flow.
        return await factory.SignInAsync(phone, role);
    }

    /// <summary>A live, approved listing owned by whoever <paramref name="seller"/> is signed in as.</summary>
    private static async Task<Guid> ApprovedListingAsync(HttpClient seller, HttpClient admin)
    {
        var created = await seller.PostAsJsonAsync("/api/v1/listings", new CreateListingRequest(
            CategorySlug, RegionSlug, "Test elan", "Test təsviri", 120m, "Used", null, false, "0501234567", true, null));

        var draft = (await created.Content.ReadFromJsonAsync<ListingDetailDto>())!;

        await seller.PostAsync($"/api/v1/listings/{draft.Id}/media",
            new MultipartFormDataContent { { JpegContent(), "file", "photo.jpg" } });
        await seller.PostAsJsonAsync($"/api/v1/listings/{draft.Id}/publish", new PublishListingRequest(false));
        await admin.PostAsync($"/api/v1/admin/moderation/{draft.Id}/approve", null);

        return draft.Id;
    }

    /// <summary>
    /// A real, decodable JPEG — the upload pipeline decodes and re-encodes what it is given, so a
    /// token byte array (a bare magic-number header) is correctly rejected as not an image at all.
    /// </summary>
    private static ByteArrayContent JpegContent()
    {
        using var image = new Image<Rgba32>(640, 480);
        using var buffer = new MemoryStream();
        image.SaveAsJpeg(buffer);

        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        return content;
    }

    private static async Task<AdminPromotionPackageDto> CreatePackageAsync(
        HttpClient admin, decimal price = 9.99m, int durationDays = 7, string? code = null)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/admin/promotion-packages", new CreatePromotionPackageRequest(
            code ?? $"pkg-{Guid.NewGuid():N}"[..12], "Test paketi", null, durationDays, price, 10));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminPromotionPackageDto>())!;
    }

    [Fact]
    public async Task The_public_catalog_starts_empty_and_shows_a_package_once_an_admin_creates_one()
    {
        await using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);

        var before = await factory.CreateApiClient().GetFromJsonAsync<List<PromotionPackageDto>>("/api/v1/promotion-packages");
        Assert.Empty(before!);

        await CreatePackageAsync(admin);

        var after = await factory.CreateApiClient().GetFromJsonAsync<List<PromotionPackageDto>>("/api/v1/promotion-packages");
        Assert.Single(after!);
    }

    [Fact]
    public async Task Creating_an_order_charges_the_servers_price_the_client_sends_a_package_id_only()
    {
        await using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);
        var seller = await SignedInAsync(factory, SellerPhone);

        var listingId = await ApprovedListingAsync(seller, admin);
        var package = await CreatePackageAsync(admin, price: 14.5m);

        var response = await seller.PostAsJsonAsync(
            $"/api/v1/me/listings/{listingId}/promotions/orders", new CreatePromotionOrderRequest(package.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreatePromotionOrderResultDto>())!;

        var order = await seller.GetFromJsonAsync<PaymentOrderDto>($"/api/v1/me/payment-orders/{created.PaymentOrderId}");
        Assert.Equal(14.5m, order!.AmountAzn);
        Assert.Equal("AwaitingPayment", order.Status);
    }

    [Fact]
    public async Task A_seller_cannot_buy_a_promotion_for_another_sellers_listing()
    {
        await using var factory = await SeededFactoryAsync();
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);
        var owner = await SignedInAsync(factory, SellerPhone);
        var other = await SignedInAsync(factory, OtherSellerPhone);

        var listingId = await ApprovedListingAsync(owner, admin);
        var package = await CreatePackageAsync(admin);

        var response = await other.PostAsJsonAsync(
            $"/api/v1/me/listings/{listingId}/promotions/orders", new CreatePromotionOrderRequest(package.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Creating_an_order_without_authentication_is_refused()
    {
        await using var factory = await SeededFactoryAsync();

        var response = await factory.CreateApiClient().PostAsJsonAsync(
            $"/api/v1/me/listings/{Guid.NewGuid()}/promotions/orders", new CreatePromotionOrderRequest(1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<(HttpClient Seller, HttpClient Admin, Guid ListingId, AdminPromotionPackageDto Package, Guid PaymentOrderId)>
        AwaitingPaymentOrderAsync(ApiFactory factory, decimal price = 9.99m)
    {
        var admin = await SignedInAsync(factory, AdminPhone, UserRole.Admin);
        var seller = await SignedInAsync(factory, SellerPhone);

        var listingId = await ApprovedListingAsync(seller, admin);
        var package = await CreatePackageAsync(admin, price: price);

        var response = await seller.PostAsJsonAsync(
            $"/api/v1/me/listings/{listingId}/promotions/orders", new CreatePromotionOrderRequest(package.Id));
        var created = (await response.Content.ReadFromJsonAsync<CreatePromotionOrderResultDto>())!;

        return (seller, admin, listingId, package, created.PaymentOrderId);
    }

    [Fact]
    public async Task A_verified_callback_activates_the_promotion()
    {
        await using var factory = await SeededFactoryAsync();
        var (seller, _, _, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        var fields = RecordingPaymentGatewayClient.BuildCallback(orderId, succeeded: true, amount: package.PriceAzn);
        var callback = await factory.CreateApiClient().PostAsync(
            "/api/v1/payments/callback/epoint", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);

        var order = await seller.GetFromJsonAsync<PaymentOrderDto>($"/api/v1/me/payment-orders/{orderId}");
        Assert.Equal("Paid", order!.Status);

        Assert.Contains("promotion.activated", await factory.GetAuditActionsAsync());
    }

    [Fact]
    public async Task A_failed_payment_never_activates_the_promotion()
    {
        await using var factory = await SeededFactoryAsync();
        var (seller, _, _, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        factory.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Failed, reference, null, "failed", "100");

        var fields = RecordingPaymentGatewayClient.BuildCallback(orderId, succeeded: false);
        var callback = await factory.CreateApiClient().PostAsync(
            "/api/v1/payments/callback/epoint", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);

        var order = await seller.GetFromJsonAsync<PaymentOrderDto>($"/api/v1/me/payment-orders/{orderId}");
        Assert.Equal("Failed", order!.Status);
        Assert.DoesNotContain("promotion.activated", await factory.GetAuditActionsAsync());
    }

    [Fact]
    public async Task A_forged_callback_signature_is_rejected_and_changes_nothing()
    {
        await using var factory = await SeededFactoryAsync();
        var (seller, _, _, _, orderId) = await AwaitingPaymentOrderAsync(factory);

        var forged = RecordingPaymentGatewayClient.BuildForgedCallback(orderId, succeeded: true);
        var callback = await factory.CreateApiClient().PostAsync(
            "/api/v1/payments/callback/epoint", new FormUrlEncodedContent(forged));

        Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);

        var order = await seller.GetFromJsonAsync<PaymentOrderDto>($"/api/v1/me/payment-orders/{orderId}");
        Assert.Equal("AwaitingPayment", order!.Status);
        Assert.DoesNotContain("promotion.activated", await factory.GetAuditActionsAsync());
    }

    [Fact]
    public async Task A_duplicate_callback_delivery_is_answered_success_but_never_activates_twice()
    {
        await using var factory = await SeededFactoryAsync();
        var (_, _, _, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        var fields = RecordingPaymentGatewayClient.BuildCallback(orderId, succeeded: true, amount: package.PriceAzn, transaction: "same-transaction");

        var first = await factory.CreateApiClient().PostAsync("/api/v1/payments/callback/epoint", new FormUrlEncodedContent(fields));
        var second = await factory.CreateApiClient().PostAsync("/api/v1/payments/callback/epoint", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var activations = (await factory.GetAuditActionsAsync()).Count(a => a == "promotion.activated");
        Assert.Equal(1, activations);
    }

    [Fact]
    public async Task An_expired_order_never_activates_a_promotion_even_with_a_valid_confirming_callback()
    {
        await using var factory = await SeededFactoryAsync();
        var (seller, _, _, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        await factory.ExpirePaymentOrderAsync(orderId);

        var fields = RecordingPaymentGatewayClient.BuildCallback(orderId, succeeded: true, amount: package.PriceAzn);
        var callback = await factory.CreateApiClient().PostAsync(
            "/api/v1/payments/callback/epoint", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);

        // Never activated — but recorded as a late capture so it can be refunded (audit M-1).
        var order = await seller.GetFromJsonAsync<PaymentOrderDto>($"/api/v1/me/payment-orders/{orderId}");
        Assert.Equal("PaidAfterExpiry", order!.Status);

        var actions = await factory.GetAuditActionsAsync();
        Assert.DoesNotContain("promotion.activated", actions);
        Assert.Contains("payment_order.late_payment_after_expiry", actions);
    }

    [Fact]
    public async Task A_capture_confirmed_after_expiry_can_be_refunded_by_an_admin()
    {
        await using var factory = await SeededFactoryAsync();
        var (seller, admin, _, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        await factory.ExpirePaymentOrderAsync(orderId);
        await factory.CreateApiClient().PostAsync("/api/v1/payments/callback/epoint",
            new FormUrlEncodedContent(RecordingPaymentGatewayClient.BuildCallback(orderId, succeeded: true, amount: package.PriceAzn)));

        var refund = await admin.PostAsJsonAsync(
            $"/api/v1/admin/payment-orders/{orderId}/refund", new RefundPaymentOrderRequest(null, "Captured after expiry"));

        Assert.Equal(HttpStatusCode.NoContent, refund.StatusCode);

        var order = await seller.GetFromJsonAsync<PaymentOrderDto>($"/api/v1/me/payment-orders/{orderId}");
        Assert.Equal("Refunded", order!.Status);
    }

    [Fact]
    public async Task An_undecided_status_re_check_is_answered_503_and_the_resent_callback_completes_the_order()
    {
        // Integration audit H-1/L-5: "new" from the gateway's status endpoint is not a failure. The
        // order stays open, the gateway is told to come back, and the resend finishes the job.
        await using var factory = await SeededFactoryAsync();
        var (seller, _, _, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        factory.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Pending, reference, null, "new", null);

        var fields = RecordingPaymentGatewayClient.BuildCallback(orderId, succeeded: true, amount: package.PriceAzn);
        var first = await factory.CreateApiClient().PostAsync("/api/v1/payments/callback/epoint", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);

        var open = await seller.GetFromJsonAsync<PaymentOrderDto>($"/api/v1/me/payment-orders/{orderId}");
        Assert.Equal("AwaitingPayment", open!.Status);

        factory.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, package.PriceAzn, "success", "000");

        var second = await factory.CreateApiClient().PostAsync("/api/v1/payments/callback/epoint", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var paid = await seller.GetFromJsonAsync<PaymentOrderDto>($"/api/v1/me/payment-orders/{orderId}");
        Assert.Equal("Paid", paid!.Status);
        Assert.Equal(1, (await factory.GetAuditActionsAsync()).Count(a => a == "promotion.activated"));
    }

    [Fact]
    public async Task A_gateway_outage_during_the_re_check_is_answered_503_and_leaves_the_order_open()
    {
        await using var factory = await SeededFactoryAsync();
        var (seller, _, _, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        factory.Gateway.NextStatusResult = _ => throw new Application.Abstractions.PaymentGatewayException("Gateway unreachable.");

        var callback = await factory.CreateApiClient().PostAsync("/api/v1/payments/callback/epoint",
            new FormUrlEncodedContent(RecordingPaymentGatewayClient.BuildCallback(orderId, succeeded: true, amount: package.PriceAzn)));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, callback.StatusCode);

        var order = await seller.GetFromJsonAsync<PaymentOrderDto>($"/api/v1/me/payment-orders/{orderId}");
        Assert.Equal("AwaitingPayment", order!.Status);
    }

    /// <summary>A fresh instance per pass, exactly as the listing-maintenance tests do.</summary>
    private static async Task RunPromotionMaintenanceAsync(ApiFactory factory)
    {
        var maintenance = new PromotionMaintenanceService(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<ILogger<PromotionMaintenanceService>>());

        await maintenance.RunOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_expiry_sweep_asks_the_gateway_first_and_completes_an_order_it_has_already_captured()
    {
        // Integration audit M-1: a customer who paid at minute 29 whose callback was lost must not
        // have that payment discovered only as a late capture that can merely be refunded.
        await using var factory = await SeededFactoryAsync();
        var (seller, _, _, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        factory.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, package.PriceAzn, "success", "000");
        factory.Clock.Advance(PromotionStateMachine.PendingOrderLifetime + TimeSpan.FromMinutes(1));

        await RunPromotionMaintenanceAsync(factory);

        var order = await seller.GetFromJsonAsync<PaymentOrderDto>($"/api/v1/me/payment-orders/{orderId}");
        Assert.Equal("Paid", order!.Status);
        Assert.Contains("promotion.activated", await factory.GetAuditActionsAsync());
        Assert.Single(factory.Gateway.StatusCheckCalls);
    }

    /// <summary>The listing row as stored, filters off — the sweep's own view of it.</summary>
    private static async Task<DateTimeOffset?> BumpedAtAsync(ApiFactory factory, Guid listingId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return (await db.Listings.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == listingId)).BumpedAt;
    }

    [Fact]
    public async Task The_sweep_re_bumps_an_active_promotions_listing_every_interval_until_it_expires()
    {
        // Integration audit L-3: a package's days buy repeated lifts, Tap.az-style, not one.
        await using var factory = await SeededFactoryAsync();
        var (seller, _, listingId, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        await factory.CreateApiClient().PostAsync("/api/v1/payments/callback/epoint",
            new FormUrlEncodedContent(RecordingPaymentGatewayClient.BuildCallback(orderId, succeeded: true, amount: package.PriceAzn)));

        var atActivation = await BumpedAtAsync(factory, listingId);
        Assert.Equal(factory.Clock.UtcNow, atActivation);

        // Seven hours in: not due yet.
        factory.Clock.Advance(TimeSpan.FromHours(7));
        await RunPromotionMaintenanceAsync(factory);
        Assert.Equal(atActivation, await BumpedAtAsync(factory, listingId));

        // Nine hours in: lifted again.
        factory.Clock.Advance(TimeSpan.FromHours(2));
        await RunPromotionMaintenanceAsync(factory);
        var afterBump = await BumpedAtAsync(factory, listingId);
        Assert.Equal(factory.Clock.UtcNow, afterBump);

        // Past the paid duration: the promotion expires in this pass and nothing lifts the listing.
        factory.Clock.Advance(TimeSpan.FromDays(package.DurationDays + 1));
        await RunPromotionMaintenanceAsync(factory);
        Assert.Equal(afterBump, await BumpedAtAsync(factory, listingId));

        var mine = await seller.GetFromJsonAsync<PagedResult<ListingSummaryDto>>("/api/v1/me/listings?status=active");
        Assert.Null(Assert.Single(mine!.Items).Promotion);
    }

    [Fact]
    public async Task The_seller_sees_the_running_promotion_on_their_own_listing()
    {
        await using var factory = await SeededFactoryAsync();
        var (seller, _, _, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        var before = await seller.GetFromJsonAsync<PagedResult<ListingSummaryDto>>("/api/v1/me/listings?status=active");
        Assert.Null(Assert.Single(before!.Items).Promotion);

        await factory.CreateApiClient().PostAsync("/api/v1/payments/callback/epoint",
            new FormUrlEncodedContent(RecordingPaymentGatewayClient.BuildCallback(orderId, succeeded: true, amount: package.PriceAzn)));

        var after = await seller.GetFromJsonAsync<PagedResult<ListingSummaryDto>>("/api/v1/me/listings?status=active");
        var promotion = Assert.Single(after!.Items).Promotion;

        Assert.NotNull(promotion);
        Assert.Equal("Active", promotion!.Status);
        Assert.Equal(PromotionStateMachine.BumpIntervalHours, promotion.BumpIntervalHours);
        Assert.Equal(promotion.ActivatedAt!.Value.AddDays(package.DurationDays), promotion.ExpiresAt);
    }

    [Fact]
    public async Task An_admin_can_open_an_order_with_the_promotion_it_funded_and_its_ledger()
    {
        await using var factory = await SeededFactoryAsync();
        var (_, admin, _, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        await factory.CreateApiClient().PostAsync("/api/v1/payments/callback/epoint",
            new FormUrlEncodedContent(RecordingPaymentGatewayClient.BuildCallback(orderId, succeeded: true, amount: package.PriceAzn)));

        var response = await admin.GetAsync($"/api/v1/admin/payment-orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        var detail = (await response.Content.ReadFromJsonAsync<AdminPaymentOrderDetailDto>())!;
        Assert.Equal("Paid", detail.Order.Status);
        Assert.Equal(package.DurationDays, detail.Order.DurationDays);
        Assert.Equal("Active", detail.Promotion!.Status);
        Assert.Contains(detail.Transactions, t => t.EventType == "OrderCreated");
        Assert.Contains(detail.Transactions, t => t.EventType == "CallbackReceived");
        Assert.Contains(detail.Transactions, t => t.EventType == "StatusChecked");

        var missing = await admin.GetAsync($"/api/v1/admin/payment-orders/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var found = await admin.GetFromJsonAsync<PagedResult<AdminPaymentOrderDto>>(
            $"/api/v1/admin/payment-orders?q={detail.Order.ProviderOrderReference}");
        Assert.Equal(orderId, Assert.Single(found!.Items).Id);

        var none = await admin.GetFromJsonAsync<PagedResult<AdminPaymentOrderDto>>("/api/v1/admin/payment-orders?q=nothing-matches-this");
        Assert.Empty(none!.Items);
    }

    [Fact]
    public async Task The_expiry_sweep_retires_an_order_the_gateway_still_cannot_decide()
    {
        await using var factory = await SeededFactoryAsync();
        var (seller, _, _, _, orderId) = await AwaitingPaymentOrderAsync(factory);

        factory.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Pending, reference, null, "new", null);
        factory.Clock.Advance(PromotionStateMachine.PendingOrderLifetime + TimeSpan.FromMinutes(1));

        await RunPromotionMaintenanceAsync(factory);

        var order = await seller.GetFromJsonAsync<PaymentOrderDto>($"/api/v1/me/payment-orders/{orderId}");
        Assert.Equal("Expired", order!.Status);
        Assert.Single(factory.Gateway.StatusCheckCalls);
        Assert.DoesNotContain("promotion.activated", await factory.GetAuditActionsAsync());
    }

    [Fact]
    public async Task An_admin_refund_reverses_the_promotion_and_a_new_purchase_becomes_possible_again()
    {
        await using var factory = await SeededFactoryAsync();
        var (seller, admin, listingId, package, orderId) = await AwaitingPaymentOrderAsync(factory);

        var fields = RecordingPaymentGatewayClient.BuildCallback(orderId, succeeded: true, amount: package.PriceAzn);
        await factory.CreateApiClient().PostAsync("/api/v1/payments/callback/epoint", new FormUrlEncodedContent(fields));

        var refund = await admin.PostAsJsonAsync(
            $"/api/v1/admin/payment-orders/{orderId}/refund", new RefundPaymentOrderRequest(null, "Buyer complaint"));

        Assert.Equal(HttpStatusCode.NoContent, refund.StatusCode);
        Assert.Contains("promotion.reversed", await factory.GetAuditActionsAsync());

        // The listing no longer has an active promotion, so a fresh purchase must be allowed again.
        var second = await seller.PostAsJsonAsync(
            $"/api/v1/me/listings/{listingId}/promotions/orders", new CreatePromotionOrderRequest(package.Id));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Refunding_an_order_that_was_never_paid_is_refused()
    {
        await using var factory = await SeededFactoryAsync();
        var (_, admin, _, _, orderId) = await AwaitingPaymentOrderAsync(factory);

        var refund = await admin.PostAsJsonAsync(
            $"/api/v1/admin/payment-orders/{orderId}/refund", new RefundPaymentOrderRequest(null, "Should be refused"));

        Assert.Equal(HttpStatusCode.Conflict, refund.StatusCode);
    }

    [Fact]
    public async Task A_non_admin_cannot_manage_promotion_packages_or_refund_orders()
    {
        await using var factory = await SeededFactoryAsync();
        var seller = await SignedInAsync(factory, SellerPhone);

        var createPackage = await seller.PostAsJsonAsync(
            "/api/v1/admin/promotion-packages", new CreatePromotionPackageRequest("nope", "Nope", null, 7, 5m, 10));
        Assert.Equal(HttpStatusCode.Forbidden, createPackage.StatusCode);

        var refund = await seller.PostAsJsonAsync(
            $"/api/v1/admin/payment-orders/{Guid.NewGuid()}/refund", new RefundPaymentOrderRequest(null, "nope"));
        Assert.Equal(HttpStatusCode.Forbidden, refund.StatusCode);
    }
}
