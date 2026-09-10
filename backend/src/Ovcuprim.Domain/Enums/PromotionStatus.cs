namespace Ovcuprim.Domain.Enums;

/// <summary>
/// Lifecycle of a listing's promotion — separate from <see cref="PaymentOrderStatus"/> by design.
/// </summary>
public enum PromotionStatus
{
    /// <summary>Row exists, funding payment has not yet been verified.</summary>
    Pending = 0,

    /// <summary>Verified payment activated this promotion; it is currently in effect.</summary>
    Active = 1,

    /// <summary>Ran its full duration. Reached only by the expiry sweep, never by a refund.</summary>
    Expired = 2,

    /// <summary>Deactivated early — today, only by a refund. Never re-enters <see cref="Active"/>.</summary>
    Reversed = 3
}
