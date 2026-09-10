using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ovcuprim.Application.Abstractions;

namespace Ovcuprim.Application.UnitTests.Payments;

/// <summary>
/// A deterministic <see cref="IPaymentGatewayClient"/> double: records every call, lets a test
/// script the next result for each operation, and can build genuinely signature-valid (or
/// deliberately forged) callback fields using its own fixed test key — so a test can exercise the
/// real <c>PaymentCallbackService</c> verification path without a network call or a real gateway.
/// </summary>
public sealed class RecordingPaymentGatewayClient : IPaymentGatewayClient
{
    public const string TestPrivateKey = "test-only-private-key";

    public string ProviderName => "Epoint";

    public List<PaymentGatewayOrderRequest> CreateOrderCalls { get; } = [];

    public List<string> StatusCheckCalls { get; } = [];

    public List<(string Reference, decimal Amount, string Reason)> RefundCalls { get; } = [];

    public Func<PaymentGatewayOrderRequest, PaymentGatewayOrderResult> NextCreateOrderResult { get; set; } =
        request => new PaymentGatewayOrderResult(true, $"test-{request.OrderId}", $"https://gateway.test/pay/{request.OrderId}", null);

    public Func<string, PaymentGatewayStatusResult> NextStatusResult { get; set; } =
        reference => new PaymentGatewayStatusResult(PaymentGatewayPaymentStatus.Paid, reference, null, "success", "000");

    public Func<string, decimal, string, PaymentGatewayRefundResult> NextRefundResult { get; set; } =
        (reference, _, _) => new PaymentGatewayRefundResult(true, reference, null);

    public Task<PaymentGatewayOrderResult> CreateOrderAsync(
        PaymentGatewayOrderRequest request, CancellationToken cancellationToken = default)
    {
        CreateOrderCalls.Add(request);
        return Task.FromResult(NextCreateOrderResult(request));
    }

    public Task<PaymentGatewayStatusResult> GetOrderStatusAsync(
        string providerOrderReference, CancellationToken cancellationToken = default)
    {
        StatusCheckCalls.Add(providerOrderReference);
        return Task.FromResult(NextStatusResult(providerOrderReference));
    }

    public Task<PaymentGatewayRefundResult> RefundAsync(
        string providerOrderReference, decimal amountAzn, string reason, CancellationToken cancellationToken = default)
    {
        RefundCalls.Add((providerOrderReference, amountAzn, reason));
        return Task.FromResult(NextRefundResult(providerOrderReference, amountAzn, reason));
    }

    public PaymentGatewayCallbackResult VerifyCallback(IReadOnlyDictionary<string, string> callbackFields)
    {
        var invalid = new PaymentGatewayCallbackResult(false, null, null, PaymentGatewayPaymentStatus.Unknown, null, null, null);

        if (!callbackFields.TryGetValue("data", out var data) || !callbackFields.TryGetValue("signature", out var signature))
        {
            return invalid;
        }

        if (!string.Equals(Sign(data), signature, StringComparison.Ordinal))
        {
            return invalid;
        }

        JsonElement payload;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(data));
            payload = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return invalid;
        }

        var orderId = payload.TryGetProperty("order_id", out var o) ? o.GetString() : null;
        var status = payload.TryGetProperty("status", out var s) ? s.GetString() : null;
        var transaction = payload.TryGetProperty("transaction", out var t) ? t.GetString() : null;
        var amount = payload.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDecimal() : (decimal?)null;

        var mapped = status?.Trim().ToLowerInvariant() switch
        {
            "success" => PaymentGatewayPaymentStatus.Paid,
            "failed" => PaymentGatewayPaymentStatus.Failed,
            "returned" => PaymentGatewayPaymentStatus.Refunded,
            _ => PaymentGatewayPaymentStatus.Unknown
        };

        return new PaymentGatewayCallbackResult(true, orderId, transaction, mapped, amount, status, status);
    }

    /// <summary>A genuinely valid, correctly signed callback for the given order and outcome.</summary>
    public static IReadOnlyDictionary<string, string> BuildCallback(
        Guid orderId, bool succeeded, decimal? amount = null, string? transaction = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["order_id"] = orderId.ToString(),
            ["status"] = succeeded ? "success" : "failed",
            ["transaction"] = transaction ?? $"test-{orderId}",
            ["amount"] = amount
        };

        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

        return new Dictionary<string, string> { ["data"] = data, ["signature"] = Sign(data) };
    }

    /// <summary>The same payload as <see cref="BuildCallback"/>, with a signature that does not verify.</summary>
    public static IReadOnlyDictionary<string, string> BuildForgedCallback(Guid orderId, bool succeeded)
    {
        var valid = BuildCallback(orderId, succeeded);
        return new Dictionary<string, string> { ["data"] = valid["data"], ["signature"] = "forged-signature-does-not-verify" };
    }

    private static string Sign(string data)
    {
        var raw = TestPrivateKey + data + TestPrivateKey;
        return Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
