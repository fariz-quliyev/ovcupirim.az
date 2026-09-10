namespace Ovcuprim.Application.Abstractions;

/// <summary>
/// The gateway's own report of where a payment stands, normalised away from any one provider's
/// vocabulary (Epoint's <c>success/failed/new/returned/error/server_error</c>, or whatever a future
/// second provider uses).
/// </summary>
public enum PaymentGatewayPaymentStatus
{
    Unknown = 0,
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Refunded = 4,
    PartiallyRefunded = 5
}

/// <summary>What OvcuPrim asks the gateway to create a hosted-checkout order for.</summary>
public sealed record PaymentGatewayOrderRequest(
    string OrderId,
    decimal AmountAzn,
    string Description,
    string SuccessRedirectUrl,
    string ErrorRedirectUrl);

public sealed record PaymentGatewayOrderResult(
    bool Succeeded,
    string? ProviderOrderReference,
    string? RedirectUrl,
    string? ErrorMessage);

public sealed record PaymentGatewayStatusResult(
    PaymentGatewayPaymentStatus Status,
    string? ProviderReference,
    decimal? AmountAzn,
    string? RawStatus,
    string? RawCode);

public sealed record PaymentGatewayRefundResult(
    bool Succeeded,
    string? ProviderReference,
    string? ErrorMessage);

/// <summary>
/// The result of verifying and decoding one callback delivery. <see cref="SignatureValid"/> is
/// checked before any of the other fields are trusted — a caller must never act on
/// <see cref="Status"/> when it is false.
/// </summary>
public sealed record PaymentGatewayCallbackResult(
    bool SignatureValid,
    string? OrderId,
    string? ProviderReference,
    PaymentGatewayPaymentStatus Status,
    decimal? AmountAzn,
    string? RawStatus,
    string? RawCode);

/// <summary>
/// The provider abstraction every gateway integration sits behind (docs/payment-integration-design.md,
/// section C). Nothing above this boundary — controllers, Application services — knows which
/// provider is configured, or anything about its wire format.
/// </summary>
public interface IPaymentGatewayClient
{
    /// <summary>Recorded on <c>PaymentOrder.Provider</c> and in every audit trail entry.</summary>
    string ProviderName { get; }

    Task<PaymentGatewayOrderResult> CreateOrderAsync(
        PaymentGatewayOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>The authoritative server-to-server re-check — never skipped, regardless of what a callback claimed.</summary>
    Task<PaymentGatewayStatusResult> GetOrderStatusAsync(
        string providerOrderReference, CancellationToken cancellationToken = default);

    Task<PaymentGatewayRefundResult> RefundAsync(
        string providerOrderReference, decimal amountAzn, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a callback's signature and decodes it. Pure and synchronous — no network call, so it
    /// can run before anything about the callback is trusted enough to look up in the database.
    /// </summary>
    PaymentGatewayCallbackResult VerifyCallback(IReadOnlyDictionary<string, string> callbackFields);
}

/// <summary>
/// A gateway operation failed to complete — the network call itself, an unexpected HTTP status, or a
/// response the adapter could not parse. Operator-facing: the message never contains a credential, a
/// signature, or card/cardholder data.
/// </summary>
public sealed class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message) : base(message)
    {
    }

    public PaymentGatewayException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
