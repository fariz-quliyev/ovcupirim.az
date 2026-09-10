using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ovcuprim.Application.Abstractions;

namespace Ovcuprim.Infrastructure.Sms;

/// <summary>
/// Reads back the code a phone would have received. Development only.
/// </summary>
/// <remarks>
/// Exists so an end-to-end test can complete a real sign-in without a gateway. The captured codes
/// live in memory, are never persisted, and the surface that exposes them is mapped only when the
/// host is running in Development.
/// </remarks>
public interface IOtpProbe
{
    /// <summary>The last code sent to this number, or null when there was none.</summary>
    string? LastCodeFor(string phoneNumber);
}

/// <summary>
/// The Development/Staging sender: records the code for the probe and logs that a message was
/// captured, without the code itself.
/// </summary>
/// <remarks>
/// <para>
/// Never registered outside Development or Staging (see <c>Program.cs</c>'s
/// <c>allowSimulatedIntegrations</c>). It used to be registered unconditionally, which would have
/// meant a production host silently sending nothing while writing one-time codes into the
/// application log — a delivery failure and a secret in the logs at the same time.
/// </para>
/// <para>
/// The logged line never carries the code, only that one was captured — deliberately, since
/// Staging's logs are not a single developer's own terminal the way Development's are, and a code
/// belongs solely in the in-memory store <see cref="IOtpProbe"/> reads, retrievable only through
/// the equally non-public <c>/api/v1/dev/otp/{phoneNumber}</c> route (see
/// <c>frontend/nginx.conf</c>'s dedicated block keeping both off the public internet). A real
/// gateway implements <see cref="ISmsSender"/> and is registered in its place; until one exists, a
/// host that is neither refuses to start rather than pretending to send.
/// </para>
/// </remarks>
public sealed class DevelopmentSmsSender(ILogger<DevelopmentSmsSender> logger) : ISmsSender, IOtpProbe
{
    private readonly ConcurrentDictionary<string, string> _lastCodes = new(StringComparer.Ordinal);

    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        var code = ExtractCode(message);

        if (code is not null)
        {
            _lastCodes[phoneNumber] = code;
        }

        logger.LogInformation(
            "SMS captured for {PhoneNumber} ({Length} chars) — read the code via IOtpProbe, never from this log.",
            phoneNumber, message.Length);

        return Task.CompletedTask;
    }

    public string? LastCodeFor(string phoneNumber) =>
        _lastCodes.TryGetValue(phoneNumber, out var code) ? code : null;

    /// <summary>The first run of ASCII digits in the message, which is how the code is written.</summary>
    private static string? ExtractCode(string message)
    {
        var digits = new string(message.SkipWhile(c => !char.IsAsciiDigit(c)).TakeWhile(char.IsAsciiDigit).ToArray());

        return digits.Length == 0 ? null : digits;
    }
}

/// <summary>
/// Stands in for a gateway that has not been chosen yet.
/// </summary>
/// <remarks>
/// Registered outside Development so the failure is loud and immediate: an OTP request throws
/// rather than succeeding while no message is delivered. Choosing and wiring a provider is a
/// launch blocker, and this makes that impossible to forget.
/// </remarks>
public sealed class UnconfiguredSmsSender : ISmsSender
{
    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "No SMS provider is configured. Register an ISmsSender implementation for this "
            + "environment; the Development sender only writes to the log and must never ship.");
}
