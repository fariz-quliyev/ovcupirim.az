using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>
/// One seller's attempt to pay for a promotion package. The authoritative, provider-agnostic payment
/// ledger — separate from <see cref="Promotion"/> by design (see
/// docs/payment-integration-design.md, section C). Nothing above the gateway adapter boundary knows
/// which provider is behind <see cref="Provider"/>.
/// </summary>
public class PaymentOrder : AuditableEntity
{
    public Guid Id { get; set; }

    public Guid SellerUserId { get; set; }

    public User SellerUser { get; set; } = null!;

    public Guid ListingId { get; set; }

    public Listing Listing { get; set; } = null!;

    public int PromotionPackageId { get; set; }

    public PromotionPackage PromotionPackage { get; set; } = null!;

    /// <summary>
    /// Snapshotted server-side from <see cref="PromotionPackage.PriceAzn"/> at creation time. The
    /// frontend sends a package id only — this is never accepted from the client.
    /// </summary>
    public decimal AmountAzn { get; set; }

    public string Currency { get; set; } = "AZN";

    /// <summary>
    /// Snapshotted from <see cref="PromotionPackage.DurationDays"/> at creation time, next to the
    /// price — what the seller was shown and paid for. Activation reads this, never the package's
    /// current value, so an administrator editing a package later cannot change what an order that
    /// already exists buys (integration audit M-5).
    /// </summary>
    public int DurationDays { get; set; }

    public PaymentOrderStatus Status { get; set; } = PaymentOrderStatus.Created;

    /// <summary>
    /// Running total of everything refunded against this order so far. Security audit finding B.3:
    /// a refund is validated against <c>AmountAzn - RefundedAmountAzn</c> (the remaining balance),
    /// never against the original amount alone — otherwise a sequence of partial refunds could
    /// together exceed what was actually captured.
    /// </summary>
    public decimal RefundedAmountAzn { get; set; }

    /// <summary>Which gateway this order was placed with. "Epoint" for this phase.</summary>
    public string Provider { get; set; } = null!;

    /// <summary>The gateway's own order/transaction identifier, populated once it accepts the order.</summary>
    public string? ProviderOrderReference { get; set; }

    /// <summary>
    /// An order still <see cref="PaymentOrderStatus.Created"/> or <see cref="PaymentOrderStatus.AwaitingPayment"/>
    /// past this instant is swept to <see cref="PaymentOrderStatus.Expired"/> and can never activate a
    /// promotion afterward, even if a late callback arrives.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token — the same application-managed pattern as <see cref="Listing.Version"/>.
    /// Guards a webhook and a status poll racing the same order.
    /// </summary>
    public long Version { get; set; }

    public Promotion? Promotion { get; set; }

    public ICollection<PaymentTransaction> Transactions { get; set; } = [];
}
