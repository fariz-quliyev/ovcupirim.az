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
/// The Development sender: records the code for the probe and writes the message to the log.
/// </summary>
/// <remarks>
/// <para>
/// Never registered outside Development. It used to be registered unconditionally, which would
/// have meant a production host silently sending nothing while writing one-time codes into the
/// application log — a delivery failure and a secret in the logs at the same time.
/// </para>
/// <para>
/// A real gateway implements <see cref="ISmsSender"/> and is registered in its place. Until one
/// exists, a non-Development host refuses to start rather than pretending to send.
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

        logger.LogInformation("SMS to {PhoneNumber}: {Message}", phoneNumber, message);

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
