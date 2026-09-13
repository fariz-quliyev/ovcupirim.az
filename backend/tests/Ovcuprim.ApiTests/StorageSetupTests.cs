using Ovcuprim.Api.Infrastructure;

namespace Ovcuprim.Api.IntegrationTests;

/// <summary>
/// The same "fail loudly at boot" treatment <c>HostFilteringSetup</c> gets, extended to the uploads
/// path: a relative root resolves against the process's own directory, which is exactly what a
/// container redeploy replaces, so a production host that has not been told otherwise is refused
/// rather than left to serve broken uploads for however long it takes someone to notice.
/// </summary>
public class StorageSetupTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wwwroot/uploads")]
    [InlineData("uploads")]
    public void A_relative_root_stops_a_production_host(string? configured)
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => StorageSetup.Validate(configured, isDevelopment: false));

        Assert.Contains("RootPath", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_resolved_absolute_is_also_refused()
    {
        // Not just the literal string "wwwroot/uploads" — its absolute equivalent under the current
        // directory is refused too, so pointing RootPath at exactly where the relative default would
        // have landed does not quietly bypass the check.
        var resolvedDefault = Path.GetFullPath("wwwroot/uploads");

        var failure = Assert.Throws<InvalidOperationException>(
            () => StorageSetup.Validate(resolvedDefault, isDevelopment: false));

        Assert.Contains("development default", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_accepts_the_relative_default()
    {
        // No exception is the assertion: Development is exempt from the absolute-path requirement,
        // the same way it is exempt from the AllowedHosts one.
        StorageSetup.Validate("wwwroot/uploads", isDevelopment: true);
    }

    [Fact]
    public void A_writable_absolute_path_outside_Development_succeeds_and_leaves_no_probe_file_behind()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ovcupirim-storage-test-{Guid.NewGuid():N}");

        try
        {
            StorageSetup.Validate(root, isDevelopment: false);

            Assert.True(Directory.Exists(root));
            Assert.Empty(Directory.EnumerateFiles(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
