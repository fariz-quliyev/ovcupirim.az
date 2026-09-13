namespace Ovcuprim.Api.Infrastructure;

/// <summary>
/// Which hostnames the API will answer to.
/// </summary>
/// <remarks>
/// <para>
/// Host filtering is the guard against a request arriving with a <c>Host</c> header we do not
/// serve — cache poisoning, password-reset links built from the header, and the general class of
/// bugs where a value from the request is trusted as the site's own address.
/// </para>
/// <para>
/// The framework treats a missing or empty <c>AllowedHosts</c> as "allow everything", and does so
/// silently: the host starts, logs nothing and accepts any name. That is the worst shape for a
/// setting whose whole job is to be restrictive, so outside Development it is required to be set,
/// and the host refuses to start without it. The real hostnames are deployment configuration and
/// are deliberately not guessed here.
/// </para>
/// </remarks>
public static class HostFilteringSetup
{
    public const string SectionName = "AllowedHosts";

    /// <summary>
    /// Throws when the configured value would let the API answer to any host outside Development.
    /// Returns the parsed host list, so a caller can log what is actually trusted.
    /// </summary>
    public static IReadOnlyList<string> Validate(string? allowedHosts, bool isDevelopment)
    {
        var hosts = (allowedHosts ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (isDevelopment)
        {
            // Local work and the integration test host reach Kestrel by whatever name they like.
            return hosts;
        }

        if (hosts.Count == 0)
        {
            throw new InvalidOperationException(
                $"{SectionName} is not configured. Set it to the hostnames this API serves, "
                + "for example \"ovcupirim.az;www.ovcupirim.az\". An empty value silently accepts any Host header.");
        }

        if (hosts.Contains("*", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{SectionName} contains \"*\", which accepts any Host header. "
                + "List the hostnames this API serves instead.");
        }

        return hosts;
    }
}
