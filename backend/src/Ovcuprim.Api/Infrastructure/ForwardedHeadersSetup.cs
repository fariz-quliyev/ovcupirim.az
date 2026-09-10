using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace Ovcuprim.Api.Infrastructure;

/// <summary>
/// Which reverse proxies the API will believe about a caller's address and scheme.
/// </summary>
/// <remarks>
/// This matters more than it looks. Every rate limit on the API partitions on
/// <c>RemoteIpAddress</c>, so behind a proxy there are exactly two ways to get it wrong: trust
/// nothing, and every visitor shares one bucket, turning "5 OTP requests per 15 minutes" into
/// five for the entire site; or trust anything, and <c>X-Forwarded-For</c> becomes a header an
/// attacker sets to whatever they like, making every limit free to bypass.
/// </remarks>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Individual proxy addresses, e.g. <c>10.0.0.4</c>. Deployment configuration — never a
    /// default, because the right value is a fact about the hosting topology and nothing else.
    /// </summary>
    public string[] KnownProxies { get; init; } = [];

    /// <summary>CIDR networks the proxies live in, e.g. <c>10.0.0.0/16</c>.</summary>
    public string[] KnownNetworks { get; init; } = [];

    /// <summary>
    /// How many proxies stand in front of the API. Only this many entries are consumed from the
    /// right of <c>X-Forwarded-For</c>, so a client-supplied prefix can never be mistaken for the
    /// caller.
    /// </summary>
    public int ForwardLimit { get; init; } = 1;
}

public static class ForwardedHeadersSetup
{
    /// <summary>
    /// Builds the options from configuration. Loopback stays trusted in Development so the local
    /// setup keeps working; outside Development nothing is trusted until it is named, which fails
    /// towards "every request looks like it came from the proxy" rather than towards a spoofable
    /// client address.
    /// </summary>
    public static ForwardedHeadersOptions BuildOptions(
        ForwardedHeadersSettings settings, bool isDevelopment, ILogger? logger = null)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = settings.ForwardLimit
        };

        // The defaults trust loopback. Clear them first so what ends up trusted is only ever what
        // this method put there.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var address in settings.KnownProxies)
        {
            if (IPAddress.TryParse(address.Trim(), out var parsed))
            {
                options.KnownProxies.Add(parsed);
            }
            else
            {
                throw new InvalidOperationException(
                    $"{ForwardedHeadersSettings.SectionName}:KnownProxies contains '{address}', which is not an IP address.");
            }
        }

        foreach (var network in settings.KnownNetworks)
        {
            options.KnownIPNetworks.Add(ParseNetwork(network));
        }

        if (isDevelopment && options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
        {
            // A developer running the API directly is their own proxy.
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
        }

        if (!isDevelopment && options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
        {
            // Not fatal — a host with no proxy in front of it is a legitimate deployment, and the
            // connection address is then the real one. But if there *is* a proxy, every rate limit
            // is silently sharing one bucket, and that is worth saying out loud at startup.
            logger?.LogWarning(
                "No {Section}:KnownProxies or {Section}:KnownNetworks are configured. Forwarded headers " +
                "will be ignored and rate limiting will partition on the connection address. If this API " +
                "sits behind a reverse proxy or load balancer, configure them.",
                ForwardedHeadersSettings.SectionName,
                ForwardedHeadersSettings.SectionName);
        }

        return options;
    }

    /// <summary>Parses <c>10.0.0.0/16</c>. Throws rather than quietly trusting a malformed entry.</summary>
    private static IPNetwork ParseNetwork(string value)
    {
        var parts = value.Trim().Split('/', StringSplitOptions.TrimEntries);

        if (parts.Length == 2
            && IPAddress.TryParse(parts[0], out var prefix)
            && int.TryParse(parts[1], out var length)
            && length >= 0
            && length <= (prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32))
        {
            return new IPNetwork(prefix, length);
        }

        throw new InvalidOperationException(
            $"{ForwardedHeadersSettings.SectionName}:KnownNetworks contains '{value}', which is not a CIDR network.");
    }
}
