using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Payments;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Payments;

public class PaymentAdminServiceTests
{
    private static async Task<Guid> PaidOrderAsync(PromotionTestHarness harness, decimal price = 15m)
    {
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync(priceAzn: price);
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, price, "success", "000");

        var fields = RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: price);
        await harness.Callbacks.ProcessCallbackAsync(fields);

        return order.Id;
    }

    [Fact]
    public async Task A_full_refund_reverses_the_active_promotion_and_never_leaves_it_active()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var orderId = await PaidOrderAsync(harness);

        harness.ActAsAdmin();
        var result = await harness.Admin.RefundAsync(orderId, new RefundPaymentOrderRequest(null, "Seller requested a refund"));

        Assert.True(result.Succeeded);

        var order = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(PaymentOrderStatus.Refunded, order.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == orderId);
        Assert.Equal(PromotionStatus.Reversed, promotion.Status);
        Assert.NotNull(promotion.ReversedAt);
        Assert.Equal("Seller requested a refund", promotion.ReversedReason);
    }

    [Fact]
    public async Task A_partial_refund_still_reverses_the_promotion()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var orderId = await PaidOrderAsync(harness, price: 20m);

        harness.ActAsAdmin();
        var result = await harness.Admin.RefundAsync(orderId, new RefundPaymentOrderRequest(5m, "Partial goodwill refund"));

        Assert.True(result.Succeeded);

        var order = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(PaymentOrderStatus.PartiallyRefunded, order.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == orderId);
        Assert.Equal(PromotionStatus.Reversed, promotion.Status);
    }

    [Fact]
    public async Task A_capture_confirmed_after_expiry_can_be_refunded_and_its_promotion_can_never_activate_afterwards()
    {
        // Integration audit M-1: money that moved is always refundable, even when the approved rule
        // says the order it paid for can no longer activate anything.
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync(priceAzn: 12m);
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        order.Status = PaymentOrderStatus.Expired;
        await harness.Db.SaveChangesAsync();

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, 12m, "success", "000");
        await harness.Callbacks.ProcessCallbackAsync(
            RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: 12m));

        var captured = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.PaidAfterExpiry, captured.Status);

        harness.ActAsAdmin();
        var result = await harness.Admin.RefundAsync(order.Id, new RefundPaymentOrderRequest(null, "Captured after the order expired"));

        Assert.True(result.Succeeded);
        Assert.Single(harness.Gateway.RefundCalls);
        Assert.Equal(12m, harness.Gateway.RefundCalls[0].Amount);

        var refunded = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(PaymentOrderStatus.Refunded, refunded.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Reversed, promotion.Status);

        // Neither the recovery sweep nor a resent callback can activate it now.
        Assert.Equal(0, await harness.Recovery.RecoverDeferredActivationsAsync());
        await harness.Callbacks.ProcessCallbackAsync(
            RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: 12m, transaction: "resend"));
        Assert.False(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "promotion.activated"));
    }

    [Fact]
    public async Task The_reconciliation_view_keeps_an_order_whose_listing_was_soft_deleted_and_exposes_its_ledger()
    {
        // Integration audit L-1 and M-3: a financial record never disappears because of the
        // listing's state, and an operator can read the whole ledger behind it.
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var orderId = await PaidOrderAsync(harness);

        var listingId = (await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == orderId)).ListingId;
        var listing = await harness.Db.Listings.SingleAsync(l => l.Id == listingId);
        listing.DeletedAt = harness.Clock.UtcNow;
        await harness.Db.SaveChangesAsync();

        harness.ActAsAdmin();
        var page = await harness.Admin.GetOrdersAsync(null, null, new PageRequest { Page = 1, PageSize = 24 });

        Assert.True(page.Succeeded);
        Assert.Equal(1, page.Value!.Total);
        var row = Assert.Single(page.Value.Items);
        Assert.Equal(orderId, row.Id);
        Assert.Equal("Paid", row.Status);
        Assert.Equal("Active", row.PromotionStatus);
        Assert.Equal(7, row.DurationDays);

        var detail = await harness.Admin.GetOrderAsync(orderId);

        Assert.True(detail.Succeeded);
        Assert.Equal("Active", detail.Value!.Promotion!.Status);
        Assert.NotNull(detail.Value.Promotion.ExpiresAt);

        var events = detail.Value.Transactions.Select(t => t.EventType).ToList();
        Assert.Contains("OrderCreated", events);
        Assert.Contains("CallbackReceived", events);
        Assert.Contains("StatusChecked", events);
        Assert.Equal(detail.Value.Transactions.OrderBy(t => t.CreatedAt).Select(t => t.Id), detail.Value.Transactions.Select(t => t.Id));

        var missing = await harness.Admin.GetOrderAsync(Guid.CreateVersion7());
        Assert.Equal(ResultError.NotFound, missing.Error);
    }

    [Fact]
    public async Task The_reconciliation_view_can_be_searched_and_refuses_an_unknown_status()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var orderId = await PaidOrderAsync(harness);
        var order = await harness.Db.PaymentOrders.AsNoTracking().Include(o => o.SellerUser).SingleAsync(o => o.Id == orderId);
        var page = new PageRequest { Page = 1, PageSize = 24 };

        harness.ActAsAdmin();

        var byReference = await harness.Admin.GetOrdersAsync(null, order.ProviderOrderReference, page);
        Assert.Single(byReference.Value!.Items);

        var byPhone = await harness.Admin.GetOrdersAsync(null, order.SellerUser.PhoneNumber[^6..], page);
        Assert.Single(byPhone.Value!.Items);

        var byStatusAndTerm = await harness.Admin.GetOrdersAsync("paid", order.ProviderOrderReference, page);
        Assert.Single(byStatusAndTerm.Value!.Items);

        var miss = await harness.Admin.GetOrdersAsync(null, "no-such-reference", page);
        Assert.Empty(miss.Value!.Items);

        var unknownStatus = await harness.Admin.GetOrdersAsync("Bogus", null, page);
        Assert.False(unknownStatus.Succeeded);
        Assert.Equal(ResultError.Validation, unknownStatus.Error);
    }

    [Fact]
    public async Task An_unpaid_order_cannot_be_refunded()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        harness.ActAsAdmin();
        var result = await harness.Admin.RefundAsync(order.Id, new RefundPaymentOrderRequest(null, "Should be refused"));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Conflict, result.Error);
    }

    [Fact]
    public async Task A_second_partial_refund_cannot_push_the_total_past_what_was_captured()
    {
        // Security audit finding B.3: the first partial refund's own check ("amount <= AmountAzn")
        // said nothing about what a PRIOR refund already took — two partials that individually look
        // fine could together exceed the original payment. This proves the second one is refused.
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var orderId = await PaidOrderAsync(harness, price: 10m);

        harness.ActAsAdmin();
        var first = await harness.Admin.RefundAsync(orderId, new RefundPaymentOrderRequest(8m, "First partial"));
        Assert.True(first.Succeeded);

        var second = await harness.Admin.RefundAsync(orderId, new RefundPaymentOrderRequest(8m, "Second partial"));

        Assert.False(second.Succeeded);
        Assert.Equal(ResultError.Validation, second.Error);

        // The first refund's own effect must stand untouched — rejecting the second is not allowed
        // to undo the first.
        var order = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(8m, order.RefundedAmountAzn);
        Assert.Equal(PaymentOrderStatus.PartiallyRefunded, order.Status);
    }

    [Fact]
    public async Task Multiple_partial_refunds_up_to_exactly_the_captured_amount_are_all_accepted()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var orderId = await PaidOrderAsync(harness, price: 10m);

        harness.ActAsAdmin();
        var first = await harness.Admin.RefundAsync(orderId, new RefundPaymentOrderRequest(4m, "First partial"));
        var second = await harness.Admin.RefundAsync(orderId, new RefundPaymentOrderRequest(6m, "Second partial, completes it"));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);

        var order = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(10m, order.RefundedAmountAzn);
        // Reaching the full amount through partials still ends the order in Refunded, not stuck
        // PartiallyRefunded — the same terminal state a single full refund would reach.
        Assert.Equal(PaymentOrderStatus.Refunded, order.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == orderId);
        Assert.Equal(PromotionStatus.Reversed, promotion.Status);
    }

    [Fact]
    public async Task A_full_refund_requested_after_an_earlier_partial_only_takes_the_remaining_balance()
    {
        // Requesting a "full refund" (Amount omitted) after a partial one already happened must mean
        // "refund what is left", not "refund the original total again".
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var orderId = await PaidOrderAsync(harness, price: 10m);

        harness.ActAsAdmin();
        await harness.Admin.RefundAsync(orderId, new RefundPaymentOrderRequest(3m, "Partial first"));
        var result = await harness.Admin.RefundAsync(orderId, new RefundPaymentOrderRequest(null, "Refund the rest"));

        Assert.True(result.Succeeded);

        var order = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(10m, order.RefundedAmountAzn);
        Assert.Equal(PaymentOrderStatus.Refunded, order.Status);
    }

    [Fact]
    public async Task A_refund_the_gateway_rejects_leaves_the_order_and_promotion_untouched()
    {
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var orderId = await PaidOrderAsync(harness);

        harness.Gateway.NextRefundResult = (reference, _, _) => new(false, reference, "gateway declined the refund");

        harness.ActAsAdmin();
        var result = await harness.Admin.RefundAsync(orderId, new RefundPaymentOrderRequest(null, "Attempted refund"));

        Assert.False(result.Succeeded);

        var order = await harness.Db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(PaymentOrderStatus.Paid, order.Status);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == orderId);
        Assert.Equal(PromotionStatus.Active, promotion.Status);
    }
}
