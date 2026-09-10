using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Notifications;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Payments;

public interface IPromotionRecoveryService
{
    /// <summary>
    /// Retries activation for every order that is genuinely <see cref="PaymentOrderStatus.Paid"/> but
    /// whose <see cref="Promotion"/> could not activate because another promotion already held the
    /// listing's one active slot at the time (see
    /// <c>PaymentCallbackService.TryActivatePromotionAsync</c> — security audit finding B.2). Never
    /// re-verifies payment with the gateway — that already happened and is not repeated here — only
    /// retries the promotion side, and only for a listing whose slot is actually free now. Returns
    /// how many orders were recovered, for the caller's own logging.
    /// </summary>
    Task<int> RecoverDeferredActivationsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Lives in Application (not alongside the <c>BackgroundService</c> that calls it on a timer) so it
/// can be exercised directly against a real database in <c>Ovcuprim.PostgresTests</c> — the failure
/// mode this recovers from is a database unique-constraint conflict the in-memory provider cannot
/// reproduce at all.
/// </summary>
public sealed class PromotionRecoveryService(
    IAppDbContext db, IDateTimeProvider clock, INotificationService notifications) : IPromotionRecoveryService
{
    public async Task<int> RecoverDeferredActivationsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        // IgnoreQueryFilters for the same reason PaymentCallbackService uses it: Listing's soft-delete
        // filter sits on a required navigation, and without this a deferred order whose listing was
        // since soft-deleted would silently vanish from the candidates and stay Pending forever
        // (integration audit L-1). A paid order is a financial record; it is never allowed to
        // disappear because of the listing's state.
        var candidates = await db.PaymentOrders
            .IgnoreQueryFilters()
            .Include(o => o.Promotion)
            .Include(o => o.PromotionPackage)
            .Include(o => o.Listing)
            .Where(o => o.Status == PaymentOrderStatus.Paid
                && o.Promotion != null && o.Promotion.Status == PromotionStatus.Pending)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var recovered = new List<PaymentOrder>();

        foreach (var order in candidates)
        {
            var promotion = order.Promotion!;

            var stillBlocked = await db.Promotions.AnyAsync(
                p => p.ListingId == order.ListingId && p.Status == PromotionStatus.Active && p.Id != promotion.Id,
                cancellationToken);

            if (stillBlocked)
            {
                continue;
            }

            promotion.Status = PromotionStatus.Active;
            promotion.ActivatedAt = now;
            promotion.ExpiresAt = now + TimeSpan.FromDays(PromotionDurations.For(order));
            order.Listing.BumpedAt = now;

            PaymentAuditTrail.RecordAudit(
                db, null, nameof(Promotion), promotion.Id.ToString(), "promotion.activated_on_recovery",
                AuditPayload.From(new { paymentOrderId = order.Id }), now);

            recovered.Add(order);
        }

        if (recovered.Count == 0)
        {
            return 0;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another recovery pass, or a fresh purchase, claimed the slot in the moment between the
            // check above and this save. Nothing lost — the next call tries again.
            return 0;
        }

        foreach (var order in recovered)
        {
            await notifications.NotifyAsync(new NotificationMessage(
                order.SellerUserId, "promotion.activated", "İrəli çəkmə aktivləşdi",
                "Ödənişiniz təsdiqləndi və elanınız irəli çəkildi.", nameof(PaymentOrder), order.Id.ToString()),
                cancellationToken);
        }

        // Staged after the save above — InAppNotificationChannel only adds each row to this same
        // context's change tracker — so it needs its own flush, or every one of them is silently lost.
        await db.SaveChangesAsync(cancellationToken);

        return recovered.Count;
    }
}
