using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>
/// Append-only record of one gateway interaction for a <see cref="PaymentOrder"/> — the same role
/// <see cref="ModerationAction"/> plays for a listing's moderation history. This is also where
/// callback idempotency is enforced: see the unique index on
/// <c>(PaymentOrderId, EventType, ProviderReference)</c> in
/// <c>PaymentTransactionConfiguration</c>.
/// </summary>
public class PaymentTransaction
{
    public Guid Id { get; set; }

    public Guid PaymentOrderId { get; set; }

    public PaymentOrder PaymentOrder { get; set; } = null!;

    public PaymentEventType EventType { get; set; }

    /// <summary>The gateway's own reference for this specific event, where it has one.</summary>
    public string? ProviderReference { get; set; }

    /// <summary>The gateway's raw status string for this event (e.g. Epoint's "success"/"failed").</summary>
    public string? ProviderStatusRaw { get; set; }

    public decimal? AmountAzn { get; set; }

    /// <summary>
    /// Structured detail for this event, built exclusively through <c>AuditPayload</c> — never a raw
    /// string assigned directly. Never contains the private key, a signature, or a full raw payload.
    /// </summary>
    public string? PayloadJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
