namespace Ovcuprim.Api.Infrastructure;

/// <summary>
/// Where uploaded media actually lives.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decision (documented, not invented):</b> local disk, behind the same <c>IFileStorage</c>
/// interface an S3-compatible provider would implement. That is a deliberate, production-usable
/// choice for a single-instance deployment with a persistent volume mounted at the configured root
/// — every listing and store image this API serves already goes through this interface, so nothing
/// about the application changes if the implementation is swapped later. It stops being sufficient
/// the moment the deployment needs more than one API instance (a second instance would not see the
/// first instance's uploads) or ships without a persistent volume (an ephemeral container filesystem
/// loses every upload on redeploy) — reaching either point is a deployment-topology decision this
/// repository is not positioned to make, so it is recorded as an open blocker in
/// docs/production-runbook.md rather than guessed at here.
/// </para>
/// <para>
/// What this class actually enforces: outside Development, the configured root must be an absolute
/// path (the relative default resolves against the process's current working directory, which for
/// most container images is inside the deployed application layer — exactly the directory a
/// redeploy replaces), and the process must be able to write to it. Both are checked once at boot,
/// on the same "a misconfigured host should fail loudly, not serve broken uploads for its first
/// hour" reasoning as the JWT signing key and the hashing key.
/// </para>
/// </remarks>
public static class StorageSetup
{
    public const string SectionName = "Storage:Local";

    /// <summary>Validates the configured root and proves the process can write to it. Throws on any failure.</summary>
    public static void Validate(string? rootPath, bool isDevelopment)
    {
        var path = string.IsNullOrWhiteSpace(rootPath) ? "wwwroot/uploads" : rootPath;

        if (!isDevelopment)
        {
            if (!Path.IsPathRooted(path))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:RootPath (\"{path}\") is not an absolute path. Outside Development this "
                    + "must point at a persistent volume mounted into the container — a relative path resolves "
                    + "against the application's own directory, which a redeploy replaces. "
                    + "Set Storage__Local__RootPath to an absolute path, for example \"/data/uploads\".");
            }

            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath("wwwroot/uploads"), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:RootPath resolves to the development default (wwwroot/uploads inside the "
                    + "application directory). Configure a path outside the deployed application layer.");
            }
        }

        Directory.CreateDirectory(path);

        // A directory that exists but is not writable (wrong ownership on the mounted volume, a
        // read-only filesystem) fails the same way a missing one would for every seller trying to
        // upload a photo — better to fail once, at boot, with a clear reason.
        var probe = Path.Combine(path, $".write-probe-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllBytes(probe, [0]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"{SectionName}:RootPath (\"{path}\") exists but is not writable by this process: {ex.Message}", ex);
        }
        finally
        {
            File.Delete(probe);
        }
    }
}
