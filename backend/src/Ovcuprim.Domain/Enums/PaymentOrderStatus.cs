namespace Ovcuprim.Domain.Enums;

/// <summary>
/// Lifecycle of a seller's payment for a promotion package. Separate from
/// <see cref="PromotionStatus"/> by design — this is "did the money move", not "is the listing
/// currently boosted".
/// </summary>
public enum PaymentOrderStatus
{
    /// <summary>Created in our database; the gateway has not been called yet.</summary>
    Created = 0,

    /// <summary>The gateway accepted the order and returned a hosted checkout URL.</summary>
    AwaitingPayment = 1,

    /// <summary>Verified server-side against the gateway. Only this state may activate a promotion.</summary>
    Paid = 2,

    /// <summary>The gateway reported the payment failed or was declined.</summary>
    Failed = 3,

    /// <summary>Never reached <see cref="Paid"/> before <c>ExpiresAt</c>. Cannot activate a promotion.</summary>
    Expired = 4,

    /// <summary>The seller (or an operator) cancelled the order before payment completed.</summary>
    Canceled = 5,

    /// <summary>A <see cref="Paid"/> order fully refunded.</summary>
    Refunded = 6,

    /// <summary>A <see cref="Paid"/> order partially refunded.</summary>
    PartiallyRefunded = 7,

    /// <summary>
    /// The gateway confirmed a capture only after the order had already been swept to
    /// <see cref="Expired"/>. Money moved, so the order is refundable exactly like <see cref="Paid"/>;
    /// but per the approved rule an expired order never activates a promotion, so
    /// <c>PromotionRecoveryService</c> ignores it and an operator refunds it (integration audit M-1).
    /// </summary>
    PaidAfterExpiry = 8
}
