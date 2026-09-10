using Ovcuprim.Application.Abstractions;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Payments;

/// <summary>
/// Writes the two records every payment/promotion state transition leaves behind, the same way
/// <c>ListingModerationService.RecordAsync</c> writes a <c>ModerationAction</c> and an
/// <c>AuditLog</c> row together. Callers still own the transaction: nothing here calls
/// <c>SaveChangesAsync</c>, so both rows land in whichever save commits the state change itself.
/// </summary>
internal static class PaymentAuditTrail
{
    /// <summary>
    /// The append-only, typed history a payment order accumulates — see <see cref="PaymentTransaction"/>.
    /// Returns the staged row so a caller whose save the idempotency index refuses can remove it
    /// again before this context saves anything else (see <c>PaymentCallbackService.TryClaimCallbackAsync</c>).
    /// </summary>
    public static PaymentTransaction RecordTransaction(
        IAppDbContext db,
        Guid paymentOrderId,
        PaymentEventType eventType,
        string? providerReference,
        string? providerStatusRaw,
        decimal? amountAzn,
        string? payloadJson,
        DateTimeOffset now)
    {
        var transaction = new PaymentTransaction
        {
            Id = Guid.CreateVersion7(),
            PaymentOrderId = paymentOrderId,
            EventType = eventType,
            ProviderReference = providerReference,
            ProviderStatusRaw = providerStatusRaw,
            AmountAzn = amountAzn,
            PayloadJson = payloadJson,
            CreatedAt = now
        };

        db.PaymentTransactions.Add(transaction);

        return transaction;
    }

    /// <summary>
    /// The generic, cross-cutting admin trail every other mutating action on this API also writes to.
    /// Returns the staged entity so a caller that goes on to retry a failed
    /// <c>SaveChangesAsync</c> in the same context — see
    /// <c>PaymentCallbackService.TryActivatePromotionAsync</c> — can remove it first if the attempt it
    /// described did not actually happen.
    /// </summary>
    public static AuditLog RecordAudit(
        IAppDbContext db,
        Guid? actorUserId,
        string entityType,
        string entityId,
        string action,
        string? payloadJson,
        DateTimeOffset now)
    {
        var entry = new AuditLog
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = actorUserId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            PayloadJson = payloadJson,
            CreatedAt = now
        };

        db.AuditLogs.Add(entry);

        return entry;
    }
}
