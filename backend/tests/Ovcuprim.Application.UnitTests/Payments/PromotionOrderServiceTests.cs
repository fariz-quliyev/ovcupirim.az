using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Payments;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Payments;

public class PromotionOrderServiceTests
{
    [Fact]
    public async Task Creating_an_order_charges_the_servers_own_package_price_not_a_client_supplied_one()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync(priceAzn: 14.50m);

        // CreatePromotionOrderRequest carries only a package id — there is no field for a client to
        // send a price through in the first place. This proves what actually gets charged.
        var result = await harness.Orders.CreateOrderAsync(listingId, new CreatePromotionOrderRequest(package.Id));

        Assert.True(result.Succeeded);
        var order = await harness.Db.PaymentOrders.FindAsync(result.Value!.PaymentOrderId);
        Assert.Equal(14.50m, order!.AmountAzn);
        Assert.Equal("AZN", order.Currency);
    }

    [Fact]
    public async Task A_successful_order_reaches_AwaitingPayment_and_records_an_OrderCreated_transaction()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();

        var result = await harness.Orders.CreateOrderAsync(listingId, new CreatePromotionOrderRequest(package.Id));

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Value!.RedirectUrl));

        var order = await harness.Db.PaymentOrders.FindAsync(result.Value.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.AwaitingPayment, order!.Status);
        Assert.NotNull(order.ProviderOrderReference);

        var transactions = harness.Db.PaymentTransactions.Where(t => t.PaymentOrderId == order.Id).ToList();
        Assert.Contains(transactions, t => t.EventType == PaymentEventType.OrderCreated);

        var promotion = await harness.Db.Promotions.SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Pending, promotion.Status);
    }

    [Fact]
    public async Task A_seller_cannot_buy_a_promotion_for_another_sellers_listing()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();

        harness.ActAsOtherUser();
        var result = await harness.Orders.CreateOrderAsync(listingId, new CreatePromotionOrderRequest(package.Id));

        // Same shape every ownership check on this API uses: a mismatch is NotFound, not Forbidden,
        // so a listing id cannot be probed for who owns it.
        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.NotFound, result.Error);
    }

    [Fact]
    public async Task An_inactive_or_unknown_package_is_refused()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();

        var result = await harness.Orders.CreateOrderAsync(listingId, new CreatePromotionOrderRequest(99999));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Validation, result.Error);
    }

    [Fact]
    public async Task A_listing_that_already_has_an_active_promotion_cannot_be_bought_again()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();

        var first = await harness.AwaitingPaymentOrderAsync(listingId, package);
        var fields = RecordingPaymentGatewayClient.BuildCallback(first.Id, succeeded: true, amount: package.PriceAzn);
        await harness.Callbacks.ProcessCallbackAsync(fields);

        var second = await harness.Orders.CreateOrderAsync(listingId, new CreatePromotionOrderRequest(package.Id));

        Assert.False(second.Succeeded);
        Assert.Equal(ResultError.Conflict, second.Error);
    }

    [Fact]
    public async Task A_second_order_is_refused_while_one_is_already_awaiting_payment_for_the_same_listing()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();

        await harness.AwaitingPaymentOrderAsync(listingId, package);

        var second = await harness.Orders.CreateOrderAsync(listingId, new CreatePromotionOrderRequest(package.Id));

        Assert.False(second.Succeeded);
        Assert.Equal(ResultError.Conflict, second.Error);
    }

    [Theory]
    [InlineData(ListingStatus.Draft)]
    [InlineData(ListingStatus.PendingModeration)]
    [InlineData(ListingStatus.Rejected)]
    [InlineData(ListingStatus.Blocked)]
    [InlineData(ListingStatus.Sold)]
    [InlineData(ListingStatus.Expired)]
    public async Task A_non_active_listing_cannot_have_a_promotion_order_created_for_it(ListingStatus status)
    {
        // Security audit finding B.1: ownership alone was not eligibility — a seller could buy a
        // promotion for their own Draft, PendingModeration, Rejected, Blocked, Sold or Expired
        // listing. This proves every one of those is refused now.
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ListingInStatusAsync(status);
        var package = await harness.ActivePackageAsync();

        harness.ActAsSeller();
        var result = await harness.Orders.CreateOrderAsync(listingId, new CreatePromotionOrderRequest(package.Id));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Conflict, result.Error);

        // No order or promotion row should exist at all — the rejection happens before any is created.
        Assert.False(await harness.Db.PaymentOrders.AnyAsync(o => o.ListingId == listingId));
        Assert.False(await harness.Db.Promotions.AnyAsync(p => p.ListingId == listingId));
    }

    [Fact]
    public async Task An_active_listing_is_still_accepted()
    {
        // The companion case to the Theory above: the gate rejects everything that is not Active,
        // but Active itself must keep working exactly as before.
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ListingInStatusAsync(ListingStatus.Active);
        var package = await harness.ActivePackageAsync();

        harness.ActAsSeller();
        var result = await harness.Orders.CreateOrderAsync(listingId, new CreatePromotionOrderRequest(package.Id));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_gateway_rejection_marks_the_order_Failed_instead_of_leaving_it_stuck()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();

        harness.Gateway.NextCreateOrderResult = _ => new(false, null, null, "declined by the bank");

        var result = await harness.Orders.CreateOrderAsync(listingId, new CreatePromotionOrderRequest(package.Id));

        Assert.False(result.Succeeded);

        var order = await harness.Db.PaymentOrders.SingleAsync(o => o.ListingId == listingId);
        Assert.Equal(PaymentOrderStatus.Failed, order.Status);
    }
}
