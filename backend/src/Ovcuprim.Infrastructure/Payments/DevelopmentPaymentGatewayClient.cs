using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ovcuprim.Application.Abstractions;

namespace Ovcuprim.Infrastructure.Payments;

/// <summary>
/// Lets a Development host (and, through it, the E2E suite) drive a full, real callback —
/// signature verification, idempotency, the authoritative status re-check, activation — without a
/// real gateway. Development only, exactly like <c>IOtpProbe</c>.
/// </summary>
public interface IPaymentGatewaySimulator
{
    /// <summary>
    /// Builds the same <c>data</c>/<c>signature</c> fields a real Epoint callback would carry for
    /// this order and outcome, signed with the Development-only key this fake also verifies with.
    /// </summary>
    IReadOnlyDictionary<string, string> BuildCallback(string orderId, bool succeeded);
}

/// <summary>
/// The Development gateway: never calls a real network endpoint, never sees a real credential.
/// <see cref="CreateOrderAsync"/> returns a redirect into this host's own dev-only "checkout"
/// page (mapped only in Development — see Program.cs) instead of a real hosted-checkout URL.
/// </summary>
/// <remarks>
/// Never registered outside Development, the same rule <c>DevelopmentSmsSender</c> follows and for
/// the same reason: a production host must fail loudly if no real gateway is configured, not
/// silently pretend every payment succeeds.
/// </remarks>
public sealed class DevelopmentPaymentGatewayClient(ILogger<DevelopmentPaymentGatewayClient> logger)
    : IPaymentGatewayClient, IPaymentGatewaySimulator
{
    /// <summary>Never a real Epoint credential — only ever compared against itself, in this process.</summary>
    private const string DevelopmentPrivateKey = "development-only-not-a-real-epoint-key";

    private sealed record DevOrderState(decimal AmountAzn, string Status);

    private readonly ConcurrentDictionary<string, DevOrderState> _orders = new(StringComparer.Ordinal);

    public string ProviderName => "Epoint";

    public Task<PaymentGatewayOrderResult> CreateOrderAsync(
        PaymentGatewayOrderRequest request, CancellationToken cancellationToken = default)
    {
        var reference = $"dev-{request.OrderId}";
        _orders[reference] = new DevOrderState(request.AmountAzn, "new");

        logger.LogInformation(
            "Development payment gateway: order {OrderId} created for {Amount} AZN.", request.OrderId, request.AmountAzn);

        return Task.FromResult(new PaymentGatewayOrderResult(
            true, reference, $"/api/v1/dev/payments/{request.OrderId}/checkout", null));
    }

    public Task<PaymentGatewayStatusResult> GetOrderStatusAsync(
        string providerOrderReference, CancellationToken cancellationToken = default)
    {
        if (!_orders.TryGetValue(providerOrderReference, out var state))
        {
            return Task.FromResult(new PaymentGatewayStatusResult(PaymentGatewayPaymentStatus.Unknown, providerOrderReference, null, null, null));
        }

        return Task.FromResult(new PaymentGatewayStatusResult(
            MapStatus(state.Status), providerOrderReference, state.AmountAzn, state.Status, state.Status == "success" ? "000" : "100"));
    }

    public Task<PaymentGatewayRefundResult> RefundAsync(
        string providerOrderReference, decimal amountAzn, string reason, CancellationToken cancellationToken = default)
    {
        if (_orders.TryGetValue(providerOrderReference, out var state))
        {
            _orders[providerOrderReference] = state with { Status = "returned" };
        }

        return Task.FromResult(new PaymentGatewayRefundResult(true, providerOrderReference, null));
    }

    public PaymentGatewayCallbackResult VerifyCallback(IReadOnlyDictionary<string, string> callbackFields)
    {
        var invalid = new PaymentGatewayCallbackResult(false, null, null, PaymentGatewayPaymentStatus.Unknown, null, null, null);

        if (!callbackFields.TryGetValue("data", out var data) || !callbackFields.TryGetValue("signature", out var signature)
            || string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(signature))
        {
            return invalid;
        }

        if (!string.Equals(EpointSigning.Sign(DevelopmentPrivateKey, data), signature, StringComparison.Ordinal))
        {
            return invalid;
        }

        System.Text.Json.JsonElement payload;

        try
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(data));
            payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            return invalid;
        }

        var orderId = payload.TryGetProperty("order_id", out var o) ? o.GetString() : null;
        var status = payload.TryGetProperty("status", out var s) ? s.GetString() : null;
        var transaction = payload.TryGetProperty("transaction", out var t) ? t.GetString() : null;
        var amount = payload.TryGetProperty("amount", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Number
            ? a.GetDecimal()
            : (decimal?)null;

        return new PaymentGatewayCallbackResult(true, orderId, transaction, MapStatus(status), amount, status, status);
    }

    /// <summary>Also updates the tracked order so a subsequent status check reflects the chosen outcome.</summary>
    public IReadOnlyDictionary<string, string> BuildCallback(string orderId, bool succeeded)
    {
        var reference = $"dev-{orderId}";
        var amount = _orders.TryGetValue(reference, out var existing) ? existing.AmountAzn : 0m;
        var status = succeeded ? "success" : "failed";

        _orders[reference] = new DevOrderState(amount, status);

        var payload = new Dictionary<string, object?>
        {
            ["order_id"] = orderId,
            ["status"] = status,
            ["code"] = succeeded ? "000" : "100",
            ["transaction"] = reference,
            ["amount"] = amount
        };

        var data = EpointSigning.EncodeData(payload);
        var signature = EpointSigning.Sign(DevelopmentPrivateKey, data);

        return new Dictionary<string, string> { ["data"] = data, ["signature"] = signature };
    }

    private static PaymentGatewayPaymentStatus MapStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "success" => PaymentGatewayPaymentStatus.Paid,
        "new" or "pending" => PaymentGatewayPaymentStatus.Pending,
        "returned" => PaymentGatewayPaymentStatus.Refunded,
        "failed" or "error" or "server_error" => PaymentGatewayPaymentStatus.Failed,
        _ => PaymentGatewayPaymentStatus.Unknown
    };
}

/// <summary>
/// Stands in for a gateway that has not been configured yet — the same role
/// <c>UnconfiguredSmsSender</c> plays. Registered outside Development so a missing configuration
/// fails loudly the first time a payment is attempted, rather than silently.
/// </summary>
public sealed class UnconfiguredPaymentGatewayClient : IPaymentGatewayClient
{
    public string ProviderName => "Unconfigured";

    public Task<PaymentGatewayOrderResult> CreateOrderAsync(
        PaymentGatewayOrderRequest request, CancellationToken cancellationToken = default) =>
        throw Unconfigured();

    public Task<PaymentGatewayStatusResult> GetOrderStatusAsync(
        string providerOrderReference, CancellationToken cancellationToken = default) =>
        throw Unconfigured();

    public Task<PaymentGatewayRefundResult> RefundAsync(
        string providerOrderReference, decimal amountAzn, string reason, CancellationToken cancellationToken = default) =>
        throw Unconfigured();

    public PaymentGatewayCallbackResult VerifyCallback(IReadOnlyDictionary<string, string> callbackFields) =>
        throw Unconfigured();

    // InvalidOperationException, not PaymentGatewayException: the Application layer's catch blocks
    // treat PaymentGatewayException as an ordinary, expected failure and answer the caller with a
    // Conflict result. A missing configuration must not be absorbed that way — it needs to surface
    // as a genuine unhandled exception, the same way UnconfiguredSmsSender's does.
    private static InvalidOperationException Unconfigured() => new(
        "No payment gateway is configured. Register an IPaymentGatewayClient implementation for this "
        + "environment; the Development gateway only simulates checkout and must never ship.");
}
