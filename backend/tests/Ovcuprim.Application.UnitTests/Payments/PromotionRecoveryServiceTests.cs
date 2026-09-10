using Microsoft.EntityFrameworkCore;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Payments;

/// <summary>
/// The recovery sweep exists for one shape only — a genuinely Paid order whose promotion lost the
/// listing's slot to a race. The real-database race itself is proven in <c>Ovcuprim.PostgresTests</c>;
/// this pins what the sweep must ignore.
/// </summary>
public class PromotionRecoveryServiceTests
{
    [Fact]
    public async Task A_capture_confirmed_after_expiry_is_never_recovered_into_an_active_promotion()
    {
        // Integration audit M-1: PaidAfterExpiry looks like "paid with a pending promotion" — exactly
        // the shape the recovery sweep hunts for — but the approved rule says an expired order never
        // activates, so the sweep must not touch it.
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        var bumpedBefore = (await harness.Db.Listings.AsNoTracking().SingleAsync(l => l.Id == listingId)).BumpedAt;

        order.Status = PaymentOrderStatus.Expired;
        await harness.Db.SaveChangesAsync();

        harness.Gateway.NextStatusResult = reference =>
            new(Application.Abstractions.PaymentGatewayPaymentStatus.Paid, reference, package.PriceAzn, "success", "000");
        await harness.Callbacks.ProcessCallbackAsync(
            RecordingPaymentGatewayClient.BuildCallback(order.Id, succeeded: true, amount: package.PriceAzn));

        var recovered = await harness.Recovery.RecoverDeferredActivationsAsync();

        Assert.Equal(0, recovered);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Pending, promotion.Status);
        Assert.Null(promotion.ActivatedAt);

        var listing = await harness.Db.Listings.AsNoTracking().SingleAsync(l => l.Id == listingId);
        Assert.Equal(bumpedBefore, listing.BumpedAt);

        Assert.False(await harness.Db.AuditLogs.AnyAsync(a => a.Action == "promotion.activated_on_recovery"));
    }

    [Fact]
    public async Task A_deferred_paid_order_is_still_recovered_when_its_listing_was_soft_deleted()
    {
        // Integration audit L-1: Listing's soft-delete filter sits on a required navigation, and
        // without IgnoreQueryFilters the sweep never saw such an order — its promotion stayed
        // Pending forever with nothing logged.
        using var harness = new PromotionTestHarness();
        await harness.SeedAsync();
        var listingId = await harness.ApprovedListingAsync();
        var package = await harness.ActivePackageAsync();
        var order = await harness.AwaitingPaymentOrderAsync(listingId, package);

        // The shape a lost race leaves behind: genuinely Paid, promotion still Pending.
        order.Status = PaymentOrderStatus.Paid;
        var listing = await harness.Db.Listings.SingleAsync(l => l.Id == listingId);
        listing.DeletedAt = harness.Clock.UtcNow;
        await harness.Db.SaveChangesAsync();

        var recovered = await harness.Recovery.RecoverDeferredActivationsAsync();

        Assert.Equal(1, recovered);

        var promotion = await harness.Db.Promotions.AsNoTracking().SingleAsync(p => p.PaymentOrderId == order.Id);
        Assert.Equal(PromotionStatus.Active, promotion.Status);
        Assert.Equal(promotion.ActivatedAt!.Value.AddDays(package.DurationDays), promotion.ExpiresAt);
    }
}
