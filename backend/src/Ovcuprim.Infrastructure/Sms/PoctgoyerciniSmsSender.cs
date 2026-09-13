using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;

namespace Ovcuprim.Infrastructure.Sms;

/// <summary>
/// Username and password for the Poctgoyercini gateway. Bound from <see cref="SectionName"/>
/// (<c>Sms__Poctgoyercini__Username</c> / <c>Sms__Poctgoyercini__Password</c> as environment
/// variables) and nothing else — no sender ID, no account ID: the gateway's <c>Send_1_N</c>
/// endpoint takes only these two plus the message and the receiver.
/// </summary>
/// <remarks>
/// Deliberately silent on which account these credentials belong to. Bumer.az already operates an
/// account with this gateway; whether that account may also send for OvcuPirim.az, or whether
/// OvcuPrim needs its own account and sender ID, is a question for the gateway and whoever holds
/// the Bumer account — not something this code assumes either way. Either answer plugs in through
/// the same two settings.
/// </remarks>
public sealed class PoctgoyerciniSmsOptions
{
    public const string SectionName = "Sms:Poctgoyercini";

    public string? Username { get; set; }

    public string? Password { get; set; }
}

/// <summary>
/// The exact body the gateway's <c>Send_1_N</c> endpoint expects — verified against Bumer.az's own
/// integration (see docs/sms-poctgoyercini.md). Property names are the wire contract, not a C#
/// naming choice, so they are pinned with <see cref="JsonPropertyNameAttribute"/> rather than left
/// to depend on whatever <see cref="JsonSerializerOptions"/> happens to be ambient wherever this is
/// serialised.
/// </summary>
internal sealed record PoctgoyerciniRequest(
    [property: JsonPropertyName("Username")] string Username,
    [property: JsonPropertyName("Password")] string Password,
    [property: JsonPropertyName("Message")] string Message,
    [property: JsonPropertyName("Receivers")] IReadOnlyList<string> Receivers);

/// <summary>Only what <see cref="PoctgoyerciniSmsSender"/> reads back — see the gateway's own StatusCode field.</summary>
internal sealed record PoctgoyerciniResponse(
    [property: JsonPropertyName("StatusCode")] int? StatusCode);

/// <summary>
/// Sends one-time codes through the Poctgoyercini gateway (<c>poctgoyercini.com</c>), the same
/// provider Bumer.az already sends through — see <c>docs/sms-poctgoyercini.md</c> for the audit
/// this was built from and the account questions that are still open with the provider.
/// </summary>
/// <remarks>
/// <para>
/// This class is a transport only. It does not generate a code, does not decide how long one is
/// valid, does not track resend cooldowns or verification attempts, and does not create or sign in
/// a user — all of that is <see cref="Ovcuprim.Application.Auth.OtpService"/>'s job, unchanged, and
/// this sender never sees an OTP value as anything other than an opaque substring of
/// <see cref="SendAsync"/>'s <c>message</c> parameter.
/// </para>
/// <para>
/// Nothing here logs a credential, a message body, or a request/response payload. Every log line
/// and every <see cref="SmsDeliveryException"/> message carries only what an operator needs to tell
/// "the network call failed" from "the gateway rejected it" from "the gateway's reply made no
/// sense" — never content that could appear in a phone's inbox or a gateway account's login form.
/// </para>
/// </remarks>
public sealed class PoctgoyerciniSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<PoctgoyerciniSmsOptions> options,
    ILogger<PoctgoyerciniSmsSender> logger) : ISmsSender
{
    /// <summary>The name this gateway's <see cref="HttpClient"/> is registered under in DI.</summary>
    public const string HttpClientName = "Poctgoyercini";

    private const string Endpoint = "https://www.poctgoyercini.com/api_json/v1/Sms/Send_1_N";

    public async Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        // The one phone-normalisation path this API has. No second implementation is introduced
        // here: whatever OvcuPrim already considers a valid Azerbaijani number is what gets sent.
        var e164 = PhoneNumber.Normalize(phoneNumber)
            ?? throw new SmsDeliveryException("Cannot send: the phone number is not a number OvcuPrim recognises as valid.");

        // The gateway's Receivers entries are digits only, no leading '+' — confirmed against
        // Bumer.az's own integration (see docs/sms-poctgoyercini.md).
        var receiver = e164.TrimStart('+');

        var settings = options.Value;
        var request = new PoctgoyerciniRequest(
            settings.Username ?? string.Empty,
            settings.Password ?? string.Empty,
            message,
            [receiver]);

        var client = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;

        try
        {
            response = await client.PostAsJsonAsync(Endpoint, request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller asked to stop. This is not a delivery failure — let it propagate as the
            // cancellation it is, not a wrapped SmsDeliveryException.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // TaskCanceledException reaches here, rather than the branch above, exactly when the
            // token itself was never cancelled — i.e. HttpClient's own request timeout fired.
            logger.LogError(ex, "SMS delivery failed: the request to the gateway did not complete.");
            throw new SmsDeliveryException("Cannot send: the request to the SMS gateway failed or timed out.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "SMS delivery failed: the gateway responded with HTTP {StatusCode}.", (int)response.StatusCode);

                throw new SmsDeliveryException($"Cannot send: the SMS gateway responded with HTTP {(int)response.StatusCode}.");
            }

            PoctgoyerciniResponse? parsed;

            try
            {
                parsed = await response.Content.ReadFromJsonAsync<PoctgoyerciniResponse>(cancellationToken);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "SMS delivery failed: the gateway's response body could not be parsed as JSON.");
                throw new SmsDeliveryException("Cannot send: the SMS gateway's response could not be parsed.", ex);
            }

            if (parsed?.StatusCode != 200)
            {
                logger.LogError(
                    "SMS delivery failed: the gateway reported StatusCode {ProviderStatusCode}.",
                    parsed?.StatusCode.ToString() ?? "(missing)");

                throw new SmsDeliveryException(
                    $"Cannot send: the SMS gateway did not report success (StatusCode={parsed?.StatusCode.ToString() ?? "missing"}).");
            }
        }
    }
}
