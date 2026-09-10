using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Payments;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Payments;

public class PaymentCallbackServiceTests
{
    [Fact]
    public async Task A_forged_signature_is_refused_and_changes_nothing()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        var forged = RecordingPaymentGatewayClient.BuildForgedCallback(order.Id, succeeded: true);
        var result = await harness.Callbacks.ProcessCallbackAsync(forged);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Validation, result.Error);

        var reloaded = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.AwaitingPayment, reloaded.Status);
    }

    [Fact]
    public async Task A_verified_callback_re_checks_status_with_the_gateway_before_activating()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync(durationDays: 7);
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, package.PriceAzn, "success", "000");

        var fields = RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn);
        var result = await harness.Callbacks.ProcessCallbackAsync(fields);

        Assert.True(result.Succeeded);
        Assert.Contains(order.ProviderOrderReference!, harness.Gateway.StatusCheckCalls);

        var reloadedOrder = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.Paid, reloadedOrder.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Active, promotion.Status);
        Assert.NotNull(promotion.ActivatedAt);
        Assert.Equal(promotion.ActivatedAt!.Value.AddDays(7), promotion.ExpiresAt);

        var listing = await harness.Db.Listings.AsNoTracking().SingleAsync(l => l.Id == listingId);
        Assert.Equal(promotion.ActivatedAt, listing.BumpedAt);
    }

    [Fact]
    public async Task A_callback_claiming_success_is_not_believed_when_the_gateways_own_status_check_disagrees()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        // The callback body says "success", but the authoritative re-check says otherwise — the
        // re-check must win, per "never trust payment success from the frontend/callback alone".
        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Failed, reference, null, "failed", "100");

        var fields = RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true);
        await harness.Callbacks.ProcessCallbackAsync(fields);

        var reloadedOrder = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.Failed, reloadedOrder.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Pending, promotion.Status);
    }

    [Fact]
    public async Task An_expired_order_never_activates_a_promotion_even_if_the_gateway_later_confirms_payment()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        order.Status = PaymentOrderStatus.Expired;
        await harness.Db.SaveChangesAsync();

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, package.PriceAzn, "success", "000");

        var fields = RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn);
        var result = await harness.Callbacks.ProcessCallbackAsync(fields);

        Assert.True(result.Succeeded);

        // The approved rule holds — nothing activates — but the money moved, so the order is recorded
        // as captured-late and refundable rather than left looking unpaid (integration audit M-1).
        var reloadedOrder = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.PaidAfterExpiry, reloadedOrder.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Pending, promotion.Status);

        Assert.True(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "payment_order.late_payment_after_expiry"));
        Assert.False(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "promotion.activated"));
        Assert.True(await harness.Db.Notifications.AnyAsync(
            n => n.UserId == harness.SellerId && n.Type == "promotion.payment_late"));
    }

    [Fact]
    public async Task A_late_callback_for_an_expired_order_that_was_never_captured_leaves_it_expired()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        order.Status = PaymentOrderStatus.Expired;
        await harness.Db.SaveChangesAsync();

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Failed, reference, null, "failed", "100");

        var result = await harness.Callbacks.ProcessCallbackAsync(
            RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn));

        Assert.True(result.Succeeded);

        var reloadedOrder = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.Expired, reloadedOrder.Status);

        Assert.True(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "payment_order.late_callback_after_expiry"));
        Assert.False(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "payment_order.late_payment_after_expiry"));
    }

    [Fact]
    public async Task A_confirmed_amount_that_does_not_match_the_order_is_refused()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync(priceAzn: 20m);
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, 1m, "success", "000");

        var fields = RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: 1m);
        var result = await harness.Callbacks.ProcessCallbackAsync(fields);

        Assert.False(result.Succeeded);

        var reloadedOrder = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.AwaitingPayment, reloadedOrder.Status);
    }

    [Fact]
    public async Task A_second_callback_after_the_order_is_already_Paid_is_a_safe_no_op()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, package.PriceAzn, "success", "000");

        var fields = RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn, transaction: "same-transaction");
        await harness.Callbacks.ProcessCallbackAsync(fields);

        // A second, distinct delivery (a different transaction id, as a resend from the gateway
        // might carry) for an order that is already Paid must not re-activate or double-notify —
        // the status-machine guard, independent of the DB-level idempotency index PostgresTests
        // proves separately.
        var secondFields = RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn, transaction: "different-transaction");
        var second = await harness.Callbacks.ProcessCallbackAsync(secondFields);

        Assert.True(second.Succeeded);

        var activatedCount = await harness.Db.AuditLogs.CountAsync(a => a.Action == "promotion.activated");
        Assert.Equal(1, activatedCount);
    }

    [Fact]
    public async Task An_unrecognised_order_id_is_answered_as_a_generic_success()
    {
        using var harness = new PromotionTestHarness();

        var fields = RecordingPaymentGatewayClient.BuildCallback(Guid.CreateVersion7(), succeeded: true);
        var result = await harness.Callbacks.ProcessCallbackAsync(fields);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_callback_still_activates_correctly_when_the_listing_has_since_been_soft_deleted()
    {
        // Nothing stops a seller from deleting a listing they just paid to promote while the payment
        // is still in flight. IgnoreQueryFilters() in ProcessCallbackAsync exists specifically so
        // payment integrity does not depend on the listing's soft-delete state — this is what proves
        // it, rather than leaving that behaviour unverified.
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        var listing = await harness.Db.Listings.SingleAsync(l => l.Id == listingId);
        listing.DeletedAt = harness.Clock.UtcNow;
        await harness.Db.SaveChangesAsync();

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, package.PriceAzn, "success", "000");

        var fields = RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn);
        var result = await harness.Callbacks.ProcessCallbackAsync(fields);

        Assert.True(result.Succeeded);

        var reloadedOrder = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.Paid, reloadedOrder.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Active, promotion.Status);

        var reloadedListing = await harness.Db.Listings.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == listingId);
        Assert.Equal(promotion.ActivatedAt, reloadedListing.BumpedAt);
    }

    [Fact]
    public async Task A_pending_status_re_check_leaves_the_order_open_and_the_resent_delivery_activates_it()
    {
        // Integration audit H-1. The gateway's status endpoint lagging its own webhook is the
        // ordinary eventual-consistency gap, not a failed payment: reading "new" as Failed would be
        // terminal, and the payment that completes a moment later would be captured with no path
        // to activation or refund.
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Pending, reference, null, "new", null);

        var fields = RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn);
        var first = await harness.Callbacks.ProcessCallbackAsync(fields);

        Assert.False(first.Succeeded);
        Assert.Equal(ResultError.Unavailable, first.Error);

        var open = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.AwaitingPayment, open.Status);
        Assert.True(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "payment_order.status_undecided"));
        Assert.False(await harness.Db.Notifications.AnyAsync(n => n.Type == "promotion.payment_failed"));

        // The gateway resends the very same delivery once it is consistent with itself.
        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, package.PriceAzn, "success", "000");

        var second = await harness.Callbacks.ProcessCallbackAsync(fields);

        Assert.True(second.Succeeded);
        Assert.Equal(2, harness.Gateway.StatusCheckCalls.Count);

        var paid = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.Paid, paid.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Active, promotion.Status);
    }

    [Fact]
    public async Task An_unrecognised_status_from_the_gateway_is_undecided_too_never_a_failure()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        // A status value the adapter has never seen — whatever the gateway adds tomorrow. Unknown
        // must never be read as "not paid".
        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Unknown, reference, null, "processing", null);

        var result = await harness.Callbacks.ProcessCallbackAsync(
            RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Unavailable, result.Error);

        var reloadedOrder = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.AwaitingPayment, reloadedOrder.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Pending, promotion.Status);
    }

    [Fact]
    public async Task A_gateway_outage_during_the_re_check_answers_unavailable_and_changes_nothing()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        harness.Gateway.NextStatusResult = _ => throw new Application.Abstractions.PaymentGatewayException("Gateway unreachable.");

        var result = await harness.Callbacks.ProcessCallbackAsync(
            RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Unavailable, result.Error);

        var reloadedOrder = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.AwaitingPayment, reloadedOrder.Status);
        Assert.True(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "payment_order.status_check_failed"));
    }

    [Fact]
    public async Task ReconcileOrderAsync_completes_an_open_order_the_gateway_has_captured_without_any_callback()
    {
        // The expiry sweep's path (integration audit M-1): a lost or delayed callback must not turn a
        // payment the gateway already holds into a late capture that can only be refunded.
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, package.PriceAzn, "success", "000");

        var result = await harness.Callbacks.ReconcileOrderAsync(order.Id);

        Assert.True(result.Succeeded);
        Assert.Single(harness.Gateway.StatusCheckCalls);

        var reloadedOrder = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.Paid, reloadedOrder.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Active, promotion.Status);

        Assert.False(await harness.Db.PaymentTransactions.AnyAsync(
            t => t.PaymentOrderId == order.Id && t.EventType == PaymentEventType.CallbackReceived));
    }

    [Fact]
    public async Task ReconcileOrderAsync_leaves_a_decided_order_alone_without_asking_the_gateway_again()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, package.PriceAzn, "success", "000");
        await harness.Callbacks.ProcessCallbackAsync(
            RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn));

        // Even a gateway that now claims the opposite cannot move a decided order.
        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Failed, reference, null, "failed", "100");

        var result = await harness.Callbacks.ReconcileOrderAsync(order.Id);

        Assert.True(result.Succeeded);
        Assert.Single(harness.Gateway.StatusCheckCalls);

        var reloadedOrder = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.Paid, reloadedOrder.Status);
    }

    [Fact]
    public async Task A_package_edit_after_purchase_does_not_change_what_the_seller_bought()
    {
        // Integration audit M-5: the duration is frozen on the order next to the price. An
        // administrator changing the package between purchase and activation must not change what
        // an existing order delivers, in either direction.
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync(durationDays: 3);
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        harness.ActAsAdmin();
        var edited = await harness.PackageAdmin.UpdateAsync(package.Id, new UpdatePromotionPackageRequest(
            package.NameAz, package.DescriptionAz, 30, package.PriceAzn, true, package.SortOrder));
        Assert.True(edited.Succeeded);

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, package.PriceAzn, "success", "000");
        await harness.Callbacks.ProcessCallbackAsync(
            RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn));

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Active, promotion.Status);
        Assert.Equal(promotion.ActivatedAt!.Value.AddDays(3), promotion.ExpiresAt);

        harness.ActAsSeller();
        var mine = await harness.Orders.GetMineByIdAsync(order.Id);
        Assert.Equal(3, mine.Value!.DurationDays);
    }

    [Fact]
    public async Task ReconcileOrderAsync_reports_an_unknown_order_as_not_found()
    {
        using var harness = new PromotionTestHarness();

        var result = await harness.Callbacks.ReconcileOrderAsync(Guid.CreateVersion7());

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.NotFound, result.Error);
    }
}
