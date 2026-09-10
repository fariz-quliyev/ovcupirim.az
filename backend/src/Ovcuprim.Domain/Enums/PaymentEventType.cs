namespace Ovcuprim.Domain.Enums;

/// <summary>
/// One entry in a <see cref="Ovcuprim.Domain.Entities.PaymentTransaction"/>'s append-only history.
/// </summary>
public enum PaymentEventType
{
    /// <summary>The gateway accepted <c>CreateOrderAsync</c> and returned a checkout URL.</summary>
    OrderCreated = 0,

    /// <summary>A callback arrived from the gateway. Never trusted on its own — see <see cref="StatusChecked"/>.</summary>
    CallbackReceived = 1,

    /// <summary>The authoritative server-to-server status re-check that gates a <c>Paid</c> transition.</summary>
    StatusChecked = 2,

    /// <summary>An operator or the seller-visible flow asked the gateway to refund.</summary>
    RefundRequested = 3,

    /// <summary>The gateway confirmed a refund completed.</summary>
    RefundConfirmed = 4,

    /// <summary>A callback or status response failed signature or identity verification.</summary>
    VerificationFailed = 5
}
