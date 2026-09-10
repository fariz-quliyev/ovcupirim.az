using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Payments;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Api.Infrastructure;

/// <summary>
/// The payment/promotion housekeeping pass: expires unpaid orders whose 30 minutes are up — after one
/// authoritative status re-check with the gateway, so an order it has already captured is completed
/// rather than retired (integration audit M-1) — retires promotions whose paid duration has run out,
/// and recovers a paid order whose own promotion lost the listing's one-active-promotion slot to a
/// race (security audit finding B.2 — see <c>PromotionRecoveryService</c> and
/// <c>PaymentCallbackService.TryActivatePromotionAsync</c>). Runs in the host for the same reason
/// <see cref="ListingMaintenanceService"/> does — no hosting package has to be added to a class library.
/// </summary>
/// <remarks>
/// Approved rule: an expired order can never later activate a promotion, even if a late callback
/// arrives — <c>PaymentCallbackService</c> checks the order's status before ever transitioning it to
/// Paid, so this sweep only has to move the status forward; it does not need to coordinate with a
/// callback that might be racing it. A capture the gateway confirms after this sweep has expired the
/// order is recorded as <see cref="PaymentOrderStatus.PaidAfterExpiry"/> and refunded, never lost.
/// </remarks>
public sealed class PromotionMaintenanceService(
    IServiceScopeFactory scopeFactory,
    ILogger<PromotionMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Promotion maintenance pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var recovery = scope.ServiceProvider.GetRequiredService<IPromotionRecoveryService>();
        var callbacks = scope.ServiceProvider.GetRequiredService<IPaymentCallbackService>();
        var now = clock.UtcNow;

        var recovered = await recovery.RecoverDeferredActivationsAsync(cancellationToken);

        if (recovered > 0)
        {
            logger.LogInformation(
                "Promotion maintenance: {Count} paid orders recovered a promotion activation deferred by a race.",
                recovered);
        }

        var dueOrders = await db.PaymentOrders
            .Where(o => (o.Status == PaymentOrderStatus.Created || o.Status == PaymentOrderStatus.AwaitingPayment)
                && o.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        var expiredOrders = 0;
        var decidedLate = 0;

        foreach (var order in dueOrders)
        {
            // Ask the gateway before retiring an order it may already have captured: a callback can
            // be delayed or lost, and a customer who paid at minute 29 must not have that payment
            // discovered only as a late capture. One authoritative attempt, through the same code a
            // callback ends in; an order still undecided afterwards — gateway silent, or not yet
            // final — expires on schedule, and a capture confirmed later is recorded as
            // PaidAfterExpiry and refunded. Nothing is ever lost, only ever refunded.
            if (order.Status == PaymentOrderStatus.AwaitingPayment && !string.IsNullOrWhiteSpace(order.ProviderOrderReference))
            {
                var reconciled = await callbacks.ReconcileOrderAsync(order.Id, cancellationToken);

                if (!reconciled.Succeeded && reconciled.Error != ResultError.Unavailable)
                {
                    logger.LogWarning(
                        "Promotion maintenance: re-check of order {OrderId} before expiry did not complete: {Message}",
                        order.Id, reconciled.Message);
                }
            }

            if (order.Status is PaymentOrderStatus.Created or PaymentOrderStatus.AwaitingPayment)
            {
                order.Status = PaymentOrderStatus.Expired;
                expiredOrders++;
            }
            else
            {
                decidedLate++;
            }
        }

        var expiredPromotions = await db.Promotions
            .Where(p => p.Status == PromotionStatus.Active && p.ExpiresAt != null && p.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var promotion in expiredPromotions)
        {
            promotion.Status = PromotionStatus.Expired;
        }

        if (expiredOrders > 0 || expiredPromotions.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var bumped = await RebumpActivePromotionsAsync(db, now, cancellationToken);

        if (expiredOrders > 0 || decidedLate > 0 || expiredPromotions.Count > 0 || bumped > 0)
        {
            logger.LogInformation(
                "Promotion maintenance: {Orders} unpaid orders expired, {Decided} decided by the gateway at expiry, {Promotions} promotions expired, {Bumped} listings re-bumped.",
                expiredOrders, decidedLate, expiredPromotions.Count, bumped);
        }
    }

    /// <summary>
    /// The repeated lift a package actually sells (integration audit L-3, Tap.az's "İrəli çək"):
    /// every <see cref="PromotionStateMachine.BumpInterval"/> while a promotion is Active, its listing
    /// goes back to the top of the default ordering by moving <c>Listing.BumpedAt</c> — the same
    /// field activation sets once. Only a publicly visible listing is lifted; a blocked, sold or
    /// expired one has nothing to be lifted in, and its promotion simply runs out.
    /// </summary>
    /// <remarks>
    /// Runs after the expiry save above so a promotion retired this very pass is not lifted one last
    /// time. Touching <c>Listing</c> bumps its concurrency token, exactly like activation does; an
    /// edit the seller has open at that instant answers 409 and is simply resubmitted.
    /// </remarks>
    private static async Task<int> RebumpActivePromotionsAsync(IAppDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = now - PromotionStateMachine.BumpInterval;

        var listings = await db.Promotions
            .Where(p => p.Status == PromotionStatus.Active
                && p.Listing.Status == ListingStatus.Active
                && (p.Listing.BumpedAt == null || p.Listing.BumpedAt <= due))
            .Select(p => p.Listing)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (listings.Count == 0)
        {
            return 0;
        }

        foreach (var listing in listings)
        {
            listing.BumpedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        return listings.Count;
    }
}
