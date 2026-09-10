using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Notifications;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Payments;

public interface IPaymentCallbackService
{
    /// <summary>
    /// Processes one callback delivery. Signature verification happens first and gates everything
    /// else; a verified callback is never trusted on its own either — it only triggers the
    /// authoritative server-to-server status re-check that actually decides whether to activate a
    /// promotion. See docs/payment-integration-design.md, section F.
    /// </summary>
    Task<Result> ProcessCallbackAsync(
        IReadOnlyDictionary<string, string> callbackFields, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same authoritative re-check <see cref="ProcessCallbackAsync"/> ends in, run without a
    /// callback — for the expiry sweep, which must ask the gateway before it retires an order the
    /// gateway may already have captured (integration audit M-1). Trusts nothing but the gateway's
    /// own status answer; an order that is already decided is left exactly as it is.
    /// </summary>
    Task<Result> ReconcileOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}

public sealed class PaymentCallbackService(
    IAppDbContext db,
    IDateTimeProvider clock,
    IPaymentGatewayClient gateway,
    INotificationService notifications) : IPaymentCallbackService
{
    public async Task<Result> ProcessCallbackAsync(
        IReadOnlyDictionary<string, string> callbackFields, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var verified = gateway.VerifyCallback(callbackFields);

        if (!verified.SignatureValid)
        {
            PaymentAuditTrail.RecordAudit(
                db, null, "PaymentCallback", "unverified", "payment.callback.signature_invalid", null, now);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Failure(ResultError.Validation, "Signature verification failed.");
        }

        if (!Guid.TryParse(verified.OrderId, out var orderId))
        {
            PaymentAuditTrail.RecordAudit(
                db, null, "PaymentCallback", verified.OrderId ?? "unknown", "payment.callback.order_id_unrecognised", null, now);
            await db.SaveChangesAsync(cancellationToken);

            // A well-signed callback for an order id shape we never issued cannot happen from the
            // real gateway; still answered as a generic success so nothing about our order-id space
            // is revealed to whatever sent it.
            return Result.Success();
        }

        var order = await LoadAsync(orderId, cancellationToken);

        if (order is null)
        {
            PaymentAuditTrail.RecordAudit(
                db, null, nameof(PaymentOrder), orderId.ToString(), "payment.callback.order_not_found", null, now);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }

        // Claim this exact delivery first, in its own save: the unique index on
        // (PaymentOrderId, CallbackReceived, ProviderReference) is what makes a re-delivered webhook
        // a safe no-op rather than a double-activation. A delivery that has no provider reference at
        // all cannot be deduplicated this way and is treated as unclaimable — recorded, not acted on.
        var claimed = await TryClaimCallbackAsync(order.Id, verified.ProviderReference, verified.RawStatus, now, cancellationToken);

        if (!claimed)
        {
            // Already recorded. For a decided order that means already acted on and the gateway is
            // simply retrying — answered as success so it stops. But a delivery whose first
            // processing ended undecided (integration audit H-1: the gateway's status endpoint had
            // not caught up with its own webhook, or was unreachable) left the order open, and the
            // retry that carries the same reference is exactly the second chance it needs — so an
            // order that is still open, or expired with a capture yet to be discovered, is
            // reconciled again rather than swallowed. Reconciling a decided order is a no-op.
            return IsDecided(order.Status)
                ? Result.Success()
                : await ReconcileAsync(order, now, cancellationToken);
        }

        return await ReconcileAsync(order, now, cancellationToken);
    }

    public async Task<Result> ReconcileOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(orderId, cancellationToken);

        return order is null
            ? Result.NotFound("Sifariş tapılmadı.")
            : await ReconcileAsync(order, clock.UtcNow, cancellationToken);
    }

    /// <summary>
    /// IgnoreQueryFilters is load-bearing: payment integrity must not depend on whether the listing
    /// has since been soft-deleted — Listing's own query filter would otherwise leave order.Listing
    /// null here and the activation step would have nothing to bump.
    /// </summary>
    private Task<PaymentOrder?> LoadAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.PaymentOrders
            .IgnoreQueryFilters()
            .Include(o => o.Promotion)
            .Include(o => o.PromotionPackage)
            .Include(o => o.Listing)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    /// <summary>
    /// A status nothing here will ever move again. <see cref="PaymentOrderStatus.Expired"/> is
    /// deliberately not in this set: it is final for activation, but a capture the gateway confirms
    /// late still has to be discovered and recorded so it can be refunded.
    /// </summary>
    private static bool IsDecided(PaymentOrderStatus status) => status is
        PaymentOrderStatus.Paid or PaymentOrderStatus.PaidAfterExpiry or PaymentOrderStatus.Failed
        or PaymentOrderStatus.Refunded or PaymentOrderStatus.PartiallyRefunded or PaymentOrderStatus.Canceled;

    /// <summary>
    /// True if this delivery had not been recorded before. A <see cref="DbUpdateException"/> here
    /// means the unique index caught a duplicate — the same technique <c>ListingQuotaService</c>
    /// uses for a racing insert, just answered as "already done" instead of "retry".
    /// </summary>
    private async Task<bool> TryClaimCallbackAsync(
        Guid orderId, string? providerReference, string? rawStatus, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerReference))
        {
            // Nothing to deduplicate on. Recorded for the trail; processing continues, since refusing
            // to ever process a reference-less callback would strand a legitimate one.
            PaymentAuditTrail.RecordTransaction(
                db, orderId, PaymentEventType.CallbackReceived, null, rawStatus, null, null, now);
            await db.SaveChangesAsync(cancellationToken);

            return true;
        }

        var claim = PaymentAuditTrail.RecordTransaction(
            db, orderId, PaymentEventType.CallbackReceived, providerReference, rawStatus, null, null, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // The staged duplicate must not ride along with the next save this context makes —
            // the reconcile that may follow for an undecided order would trip the same index again.
            db.PaymentTransactions.Remove(claim);
            return false;
        }
    }

    /// <summary>
    /// The authoritative step: re-checks status directly with the gateway and only ever transitions
    /// to <see cref="PaymentOrderStatus.Paid"/> (or, for an already-expired order,
    /// <see cref="PaymentOrderStatus.PaidAfterExpiry"/>) from that re-check — never from the callback
    /// body alone. Only a final answer decides anything: a gateway that says "not yet", or cannot be
    /// reached, leaves the order exactly where it was and reports <see cref="ResultError.Unavailable"/>
    /// so the caller knows to come back (integration audit H-1).
    /// </summary>
    private async Task<Result> ReconcileAsync(PaymentOrder order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A terminal or already-decided state: nothing left to do. Covers a second callback for an
        // order this process already marked Paid/Failed/Refunded from an earlier delivery.
        if (IsDecided(order.Status))
        {
            return Result.Success();
        }

        // Created but never accepted by the gateway: there is no reference to ask about, so nothing
        // can have been captured. For an expired order this is a stray callback with nothing to do.
        if (string.IsNullOrWhiteSpace(order.ProviderOrderReference))
        {
            return Result.Success();
        }

        PaymentGatewayStatusResult status;

        try
        {
            status = await gateway.GetOrderStatusAsync(order.ProviderOrderReference, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            PaymentAuditTrail.RecordAudit(
                db, null, nameof(PaymentOrder), order.Id.ToString(), "payment_order.status_check_failed",
                AuditPayload.From(new { error = ex.Message }), now);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Unavailable("Status re-check failed.");
        }

        PaymentAuditTrail.RecordTransaction(
            db, order.Id, PaymentEventType.StatusChecked, status.ProviderReference ?? order.ProviderOrderReference,
            status.RawStatus, status.AmountAzn, AuditPayload.From(new { code = status.RawCode }), now);

        switch (status.Status)
        {
            case PaymentGatewayPaymentStatus.Paid:
                break;

            case PaymentGatewayPaymentStatus.Failed or PaymentGatewayPaymentStatus.Refunded:
                return await FailAsync(order, status, now, cancellationToken);

            default:
                // Pending, Unknown — the gateway has not given a final answer (its status endpoint
                // lagging its own webhook is the ordinary case), or gave one this adapter has never
                // seen. Neither is a failure: mapping them to Failed would be terminal and would
                // strand a payment that completes a moment later. Recorded, left open, retried —
                // by the gateway's own resend and by the expiry sweep.
                PaymentAuditTrail.RecordAudit(
                    db, null, nameof(PaymentOrder), order.Id.ToString(), "payment_order.status_undecided",
                    AuditPayload.From(new { rawStatus = status.RawStatus, code = status.RawCode }), now);
                await db.SaveChangesAsync(cancellationToken);

                return Result.Unavailable("Payment status is not final yet.");
        }

        // The amount the gateway actually confirms must match what we asked it to collect — a
        // mismatch is refused rather than trusted, exactly like the identity check above.
        if (status.AmountAzn is { } confirmedAmount && confirmedAmount != order.AmountAzn)
        {
            PaymentAuditTrail.RecordAudit(
                db, null, nameof(PaymentOrder), order.Id.ToString(), "payment_order.amount_mismatch",
                AuditPayload.From(new { expected = order.AmountAzn, confirmed = confirmedAmount }), now);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Failure(ResultError.Conflict, "Amount mismatch.");
        }

        if (order.Status == PaymentOrderStatus.Expired)
        {
            return await RecordLateCaptureAsync(order, now, cancellationToken);
        }

        // The verified Paid transition is committed on its own, independent of promotion activation
        // below. This is the fix for security audit finding B.2: two orders for the same listing can
        // race to activate (CreateOrderAsync's own pre-check cannot fully close that window), and the
        // database's unique index on Promotions.ListingId correctly refuses the loser — but that must
        // never take the loser's already-verified Paid status down with it. Splitting the save in two
        // means a payment Epoint genuinely captured is never rolled back because of a promotion-slot
        // conflict that is really about a different concern entirely.
        order.Status = PaymentOrderStatus.Paid;

        PaymentAuditTrail.RecordAudit(
            db, null, nameof(PaymentOrder), order.Id.ToString(), "payment_order.paid",
            AuditPayload.From(new { amountAzn = order.AmountAzn }), now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ResultError.Conflict, "Order changed concurrently.");
        }

        if (order.Promotion is not { Status: PromotionStatus.Pending } promotion)
        {
            return Result.Success();
        }

        await TryActivatePromotionAsync(order, promotion, now, cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// The gateway's final answer is "not paid". An open order becomes Failed and the seller hears
    /// about it; an already-expired order is simply left expired — the callback was late and nothing
    /// was captured, which is worth a trail entry and nothing more.
    /// </summary>
    private async Task<Result> FailAsync(
        PaymentOrder order, PaymentGatewayStatusResult status, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (order.Status == PaymentOrderStatus.Expired)
        {
            PaymentAuditTrail.RecordAudit(
                db, null, nameof(PaymentOrder), order.Id.ToString(), "payment_order.late_callback_after_expiry",
                AuditPayload.From(new { rawStatus = status.RawStatus }), now);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }

        order.Status = PaymentOrderStatus.Failed;

        PaymentAuditTrail.RecordAudit(
            db, null, nameof(PaymentOrder), order.Id.ToString(), "payment_order.failed",
            AuditPayload.From(new { rawStatus = status.RawStatus }), now);

        // Staged before the save, not after: InAppNotificationChannel only adds the row to this
        // same context's change tracker — nothing else was ever going to flush it if it were
        // staged later.
        await notifications.NotifyAsync(new NotificationMessage(
            order.SellerUserId, "promotion.payment_failed", "Ödəniş uğursuz oldu",
            "İrəli çəkmə paketi üçün ödəniş tamamlanmadı.", nameof(PaymentOrder), order.Id.ToString()),
            cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ResultError.Conflict, "Order changed concurrently.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Approved rule: an expired order never activates a promotion, even if the gateway later
    /// confirms payment. But the money did move, so the order is marked
    /// <see cref="PaymentOrderStatus.PaidAfterExpiry"/> — refundable through the ordinary admin refund
    /// path, invisible to <c>PromotionRecoveryService</c> — and both the trail and the seller record
    /// that a refund, not an activation, is what follows (integration audit M-1).
    /// </summary>
    private async Task<Result> RecordLateCaptureAsync(PaymentOrder order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        order.Status = PaymentOrderStatus.PaidAfterExpiry;

        PaymentAuditTrail.RecordAudit(
            db, null, nameof(PaymentOrder), order.Id.ToString(), "payment_order.late_payment_after_expiry",
            AuditPayload.From(new { providerOrderReference = order.ProviderOrderReference, amountAzn = order.AmountAzn }), now);

        await notifications.NotifyAsync(new NotificationMessage(
            order.SellerUserId, "promotion.payment_late", "Ödəniş gec təsdiqləndi",
            "Sifarişin vaxtı bitdikdən sonra ödəniş təsdiqləndi. İrəli çəkmə aktivləşmədi, məbləğ geri qaytarılacaq.",
            nameof(PaymentOrder), order.Id.ToString()),
            cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(ResultError.Conflict, "Order changed concurrently.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Attempts to activate the promotion this now-Paid order funds. A pre-check avoids attempting a
    /// write already known to lose the listing's one-active-promotion slot; the unique index itself —
    /// never weakened — is still the authoritative backstop for the narrow remaining window between
    /// that check and this save. Losing either way is not an error: the order stays correctly Paid,
    /// and <c>PromotionMaintenanceService</c> retries this exact activation later, once whichever
    /// promotion currently holds the slot expires or is reversed.
    /// </summary>
    private async Task TryActivatePromotionAsync(
        PaymentOrder order, Promotion promotion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var blockedByAnother = await db.Promotions.AnyAsync(
            p => p.ListingId == order.ListingId && p.Status == PromotionStatus.Active && p.Id != promotion.Id,
            cancellationToken);

        // The duration the seller paid for, frozen on the order — never the package's current value.
        var activated = !blockedByAnother
            && await TrySaveActivationAsync(promotion, PromotionDurations.For(order), now, cancellationToken);

        if (activated)
        {
            // Bumping the listing's sort position is a UX nicety, not a correctness or financial
            // concern — it is deliberately its own, separate save so that a Listing.Version conflict
            // (an unrelated edit landing at the same instant, or — before this split existed — the
            // losing side of this very race touching the same listing) can never be confused with, or
            // interfere with, the promotion-activation save above. A failure here changes nothing
            // about the promotion being correctly Active.
            try
            {
                order.Listing.BumpedAt = now;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Not critical: the listing simply does not move up in sort order at this exact
                // instant. Nothing to revert — this save touched nothing else.
            }

            // Staged after both saves above (the split that keeps them independent — see remarks —
            // means the notification cannot simply ride along with either one), so it needs its own
            // flush: InAppNotificationChannel only adds the row to this context's change tracker.
            await notifications.NotifyAsync(new NotificationMessage(
                order.SellerUserId, "promotion.activated", "İrəli çəkmə aktivləşdi",
                "Ödənişiniz təsdiqləndi və elanınız irəli çəkildi.", nameof(PaymentOrder), order.Id.ToString()),
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            return;
        }

        // Genuinely paid, but this listing's one active-promotion slot currently belongs to a
        // different order. Recorded, not activated — payment.paid already committed above, so this
        // order can never be mistaken for unpaid, and it is left exactly where
        // PromotionRecoveryService's sweep will find and retry it.
        PaymentAuditTrail.RecordAudit(
            db, null, nameof(Promotion), promotion.Id.ToString(), "promotion.activation_deferred",
            AuditPayload.From(new { reason = "another promotion is already active for this listing" }), now);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Attempts the one write that actually matters for security audit finding B.2: activating
    /// <paramref name="promotion"/> alone, touching nothing else, so the only way this save can fail
    /// is the database's own unique index on <c>Promotions.ListingId</c> — never an unrelated
    /// concurrency token on a different entity. False means the slot was lost; the promotion is left
    /// exactly as it was (<see cref="PromotionStatus.Pending"/>), never partially applied.
    /// </summary>
    private async Task<bool> TrySaveActivationAsync(
        Promotion promotion, int packageDurationDays, DateTimeOffset now, CancellationToken cancellationToken)
    {
        promotion.Status = PromotionStatus.Active;
        promotion.ActivatedAt = now;
        promotion.ExpiresAt = now + TimeSpan.FromDays(packageDurationDays);

        var activatedEntry = PaymentAuditTrail.RecordAudit(
            db, null, nameof(Promotion), promotion.Id.ToString(), "promotion.activated", null, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost the race in the window between the caller's check and this save — the unique
            // index caught it. Revert in memory so the caller's own fall-through save persists only
            // its "deferred" audit row, not a second attempt at this same conflicting update.
            promotion.Status = PromotionStatus.Pending;
            promotion.ActivatedAt = null;
            promotion.ExpiresAt = null;
            db.AuditLogs.Remove(activatedEntry);

            return false;
        }
    }
}
