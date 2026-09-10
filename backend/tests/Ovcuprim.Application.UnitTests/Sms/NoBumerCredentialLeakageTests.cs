using System.Runtime.CompilerServices;

namespace Ovcuprim.Application.UnitTests.Sms;

/// <summary>
/// A separate, read-only audit of Bumer.az identified <c>bumer_s</c> as the real, live account
/// username on the Poctgoyercini gateway, and found the password paired with it exposed in Bumer's
/// own historical source and configuration — a credential that must be treated as compromised and
/// must never be copied into this repository (see docs/sms-poctgoyercini.md).
/// </summary>
/// <remarks>
/// The username alone is not a secret and is fine to reference in prose — <c>docs/</c> does exactly
/// that, for provenance. What this test guards against is someone later taking a shortcut while
/// wiring up real configuration: pasting that identifier, or a value copied from Bumer's old files,
/// directly into application source or a test fixture instead of into an environment variable. If
/// this test ever fails, the fix is to remove whatever was hardcoded, not to adjust the test.
/// </remarks>
public class NoBumerCredentialLeakageTests
{
    private const string BumerAccountUsername = "bumer_s";

    private static readonly string[] ScannedDirectories =
    [
        "backend/src",
        "backend/tests",
        "frontend/src",
    ];

    private static readonly string[] ScannedRootFiles =
    [
        ".env.example",
    ];

    [Fact]
    public void The_Bumer_account_username_never_appears_hardcoded_in_application_source_or_fixtures()
    {
        var repositoryRoot = FindRepositoryRoot();
        var thisFile = Path.GetFullPath(ThisFilePath());
        var offenders = new List<string>();

        foreach (var relativeDirectory in ScannedDirectories)
        {
            var directory = Path.Combine(repositoryRoot, relativeDirectory);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                // This file is the one place the identifier is expected to appear — it is what
                // is being searched for, not a leak of it.
                if (string.Equals(Path.GetFullPath(file), thisFile, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsBinaryOrIrrelevant(file))
                {
                    continue;
                }

                CheckFile(file, repositoryRoot, offenders);
            }
        }

        foreach (var relativeFile in ScannedRootFiles)
        {
            var file = Path.Combine(repositoryRoot, relativeFile);

            if (File.Exists(file))
            {
                CheckFile(file, repositoryRoot, offenders);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The Bumer.az account identifier appears hardcoded in: " + string.Join(", ", offenders)
            + ". It must only ever be set through configuration (Sms__Poctgoyercini__Username), never in source.");
    }

    private static void CheckFile(string file, string repositoryRoot, List<string> offenders)
    {
        var content = File.ReadAllText(file);

        if (content.Contains(BumerAccountUsername, StringComparison.OrdinalIgnoreCase))
        {
            offenders.Add(Path.GetRelativePath(repositoryRoot, file));
        }
    }

    private static bool IsBinaryOrIrrelevant(string file)
    {
        var extension = Path.GetExtension(file);

        // node_modules, bin/, obj/ and dist/ never belong in a source-leak scan — they are
        // generated, third-party, or both, and scanning them only slows the test down.
        if (file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || file.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".ico" or ".woff" or ".woff2"
            or ".ttf" or ".eot" or ".pdf" or ".dll" or ".exe" or ".zip" or ".gz";
    }

    /// <summary>Walks up from this test file's own compiled-in source path to the repository root.</summary>
    private static string FindRepositoryRoot()
    {
        // backend/tests/Ovcuprim.Application.UnitTests/Sms/<this file>.cs -> repository root is
        // four directories up.
        var directory = Path.GetDirectoryName(ThisFilePath())!;

        for (var i = 0; i < 4; i++)
        {
            directory = Path.GetDirectoryName(directory)!;
        }

        return directory;
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
