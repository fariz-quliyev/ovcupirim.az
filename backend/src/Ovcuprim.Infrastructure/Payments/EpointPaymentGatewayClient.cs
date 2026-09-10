using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;

namespace Ovcuprim.Infrastructure.Payments;

/// <summary>
/// Credentials and endpoint configuration for the Epoint gateway. Bound from <see cref="SectionName"/>
/// (<c>Payments__Epoint__PublicKey</c> / <c>Payments__Epoint__PrivateKey</c> as environment
/// variables) — never hardcoded, never present outside configuration/secrets.
/// </summary>
/// <remarks>
/// <see cref="CreateOrderPath"/>/<see cref="StatusPath"/>/<see cref="RefundPath"/> default to the
/// convention published by Epoint's community PHP SDKs (<c>rafoabbas/epoint-php</c>,
/// <c>TuralAsgar/epoint</c>) — the official developer portal's own reference pages mask their exact
/// endpoint paths behind an authenticated "reveal" control that this research could not pass. Treat
/// these defaults as unverified until confirmed against an authenticated Epoint merchant account —
/// see docs/payments-epoint.md, "open questions before production".
/// </remarks>
public sealed class EpointGatewayOptions
{
    public const string SectionName = "Payments:Epoint";

    public string? PublicKey { get; set; }

    public string? PrivateKey { get; set; }

    public string BaseUrl { get; set; } = "https://epoint.az";

    public string CreateOrderPath { get; set; } = "/api/1/request";

    public string StatusPath { get; set; } = "/api/1/get-status";

    public string RefundPath { get; set; } = "/api/1/refund-request";
}

/// <summary>
/// Epoint's <c>data</c>/<c>signature</c> scheme (docs/payment-integration-design.md, section A #5):
/// <c>data</c> is base64(JSON), <c>signature</c> is base64(sha1(private_key + data + private_key)).
/// The same scheme signs outbound requests and verifies inbound callbacks.
/// </summary>
internal static class EpointSigning
{
    private static readonly JsonSerializerOptions EncodeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Field names are the wire contract (snake_case) and are supplied by the caller as-is.</summary>
    public static string EncodeData(IReadOnlyDictionary<string, object?> payload) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, EncodeOptions)));

    public static string Sign(string privateKey, string dataBase64)
    {
        var raw = privateKey + dataBase64 + privateKey;
        return Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}

internal sealed record EpointCreateOrderResponse(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("redirect_url")] string? RedirectUrl,
    [property: JsonPropertyName("transaction")] string? Transaction,
    [property: JsonPropertyName("message")] string? Message);

internal sealed record EpointStatusResponse(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("transaction")] string? Transaction,
    [property: JsonPropertyName("amount")] decimal? Amount);

internal sealed record EpointRefundResponse(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("transaction")] string? Transaction);

/// <summary>The decoded shape of a verified callback body — see docs.epoint.az "Callbacks".</summary>
internal sealed record EpointCallbackPayload(
    [property: JsonPropertyName("order_id")] string? OrderId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("transaction")] string? Transaction,
    [property: JsonPropertyName("amount")] decimal? Amount);

/// <summary>
/// Epoint (<c>epoint.az</c>) behind <see cref="IPaymentGatewayClient"/>. A pure transport: it does
/// not decide when a promotion activates, does not store card data (hosted checkout — see
/// docs/payment-integration-design.md), and every request/response detail is documented in
/// docs/payments-epoint.md.
/// </summary>
/// <remarks>
/// Nothing here ever logs the public/private key, a signature, a raw <c>data</c> payload, or
/// cardholder detail — only status codes, gateway status strings, and the gateway's own
/// correlation identifiers, following the same discipline <c>PoctgoyerciniSmsSender</c> already
/// established for the SMS gateway.
/// </remarks>
public sealed class EpointPaymentGatewayClient(
    IHttpClientFactory httpClientFactory,
    IOptions<EpointGatewayOptions> options,
    ILogger<EpointPaymentGatewayClient> logger) : IPaymentGatewayClient
{
    public const string HttpClientName = "Epoint";

    public string ProviderName => "Epoint";

    public async Task<PaymentGatewayOrderResult> CreateOrderAsync(
        PaymentGatewayOrderRequest request, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        var payload = new Dictionary<string, object?>
        {
            ["public_key"] = settings.PublicKey,
            ["amount"] = request.AmountAzn.ToString("0.00", CultureInfo.InvariantCulture),
            ["currency"] = "AZN",
            ["language"] = "az",
            ["order_id"] = request.OrderId,
            ["description"] = request.Description,
            ["success_redirect_url"] = request.SuccessRedirectUrl,
            ["error_redirect_url"] = request.ErrorRedirectUrl
        };

        var response = await PostAsync<EpointCreateOrderResponse>(settings.CreateOrderPath, payload, cancellationToken);

        if (response is null || !string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(response.RedirectUrl))
        {
            return new PaymentGatewayOrderResult(false, response?.Transaction, null, response?.Message ?? "Gateway rejected the order.");
        }

        return new PaymentGatewayOrderResult(true, response.Transaction, response.RedirectUrl, null);
    }

    public async Task<PaymentGatewayStatusResult> GetOrderStatusAsync(
        string providerOrderReference, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        var payload = new Dictionary<string, object?>
        {
            ["public_key"] = settings.PublicKey,
            ["transaction"] = providerOrderReference
        };

        var response = await PostAsync<EpointStatusResponse>(settings.StatusPath, payload, cancellationToken)
            ?? throw new PaymentGatewayException("Epoint status check returned an empty response.");

        return new PaymentGatewayStatusResult(MapStatus(response.Status), response.Transaction, response.Amount, response.Status, response.Code);
    }

    public async Task<PaymentGatewayRefundResult> RefundAsync(
        string providerOrderReference, decimal amountAzn, string reason, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        var payload = new Dictionary<string, object?>
        {
            ["public_key"] = settings.PublicKey,
            ["transaction"] = providerOrderReference,
            ["amount"] = amountAzn.ToString("0.00", CultureInfo.InvariantCulture)
        };

        var response = await PostAsync<EpointRefundResponse>(settings.RefundPath, payload, cancellationToken);

        if (response is null || !string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentGatewayRefundResult(false, null, response?.Message ?? "Refund rejected.");
        }

        return new PaymentGatewayRefundResult(true, response.Transaction, null);
    }

    public PaymentGatewayCallbackResult VerifyCallback(IReadOnlyDictionary<string, string> callbackFields)
    {
        var invalid = new PaymentGatewayCallbackResult(false, null, null, PaymentGatewayPaymentStatus.Unknown, null, null, null);

        if (!callbackFields.TryGetValue("data", out var data) || !callbackFields.TryGetValue("signature", out var signature)
            || string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(signature))
        {
            return invalid;
        }

        var privateKey = options.Value.PrivateKey;

        if (string.IsNullOrWhiteSpace(privateKey))
        {
            logger.LogError("Cannot verify an Epoint callback: no private key is configured.");
            return invalid;
        }

        var expectedSignature = EpointSigning.Sign(privateKey, data);

        // Fixed-time comparison: the one place a leaked timing signal on this class would matter.
        if (!FixedTimeEquals(expectedSignature, signature))
        {
            return invalid;
        }

        EpointCallbackPayload? payload;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(data));
            payload = JsonSerializer.Deserialize<EpointCallbackPayload>(json);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            logger.LogError(ex, "A signature-valid Epoint callback could not be decoded.");
            return invalid;
        }

        if (payload is null)
        {
            return invalid;
        }

        return new PaymentGatewayCallbackResult(
            true, payload.OrderId, payload.Transaction, MapStatus(payload.Status), payload.Amount, payload.Status, payload.Code);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);

        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static PaymentGatewayPaymentStatus MapStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "success" => PaymentGatewayPaymentStatus.Paid,
        "new" or "pending" => PaymentGatewayPaymentStatus.Pending,
        "returned" => PaymentGatewayPaymentStatus.Refunded,
        "failed" or "error" or "server_error" => PaymentGatewayPaymentStatus.Failed,
        _ => PaymentGatewayPaymentStatus.Unknown
    };

    private async Task<TResponse?> PostAsync<TResponse>(
        string path, Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var data = EpointSigning.EncodeData(payload);
        var signature = EpointSigning.Sign(settings.PrivateKey ?? string.Empty, data);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["data"] = data,
            ["signature"] = signature
        });

        var client = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;

        try
        {
            response = await client.PostAsync(path, form, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Epoint gateway request to {Path} did not complete.", path);
            throw new PaymentGatewayException("Cannot reach the Epoint gateway.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Epoint gateway responded with HTTP {StatusCode} for {Path}.", (int)response.StatusCode, path);
                throw new PaymentGatewayException($"Epoint gateway responded with HTTP {(int)response.StatusCode}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Epoint gateway response for {Path} could not be parsed.", path);
                throw new PaymentGatewayException("Epoint gateway response could not be parsed.", ex);
            }
        }
    }
}
