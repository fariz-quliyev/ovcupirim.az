using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>
/// The marketplace-facing state of a listing's promotion — deliberately separate from
/// <see cref="PaymentOrder"/>'s payment-ledger state (see docs/payment-integration-design.md,
/// section C). <see cref="Listing.Status"/> is never read or written by anything here: a listing
/// stays exactly whatever moderation status it already has, and a promotion is purely additive.
/// </summary>
public class Promotion
{
    public Guid Id { get; set; }

    public Guid ListingId { get; set; }

    public Listing Listing { get; set; } = null!;

    public int PromotionPackageId { get; set; }

    public PromotionPackage PromotionPackage { get; set; } = null!;

    /// <summary>
    /// The order that funded this promotion. Nullable so a future non-paid, admin-granted promotion
    /// is possible without a schema change; every promotion created by this phase always has one.
    /// </summary>
    public Guid? PaymentOrderId { get; set; }

    public PaymentOrder? PaymentOrder { get; set; }

    public PromotionStatus Status { get; set; } = PromotionStatus.Pending;

    /// <summary>Set only by the verified-payment activation path, never by anything else.</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? ReversedAt { get; set; }

    public string? ReversedReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
