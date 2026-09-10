namespace Ovcuprim.Infrastructure.Storage;

public sealed class LocalFileStorageOptions
{
    public const string SectionName = "Storage:Local";

    /// <summary>Absolute or content-root-relative directory that holds uploaded media.</summary>
    public string RootPath { get; set; } = "wwwroot/uploads";

    /// <summary>URL prefix the files are served under.</summary>
    public string PublicBaseUrl { get; set; } = "/uploads";
}
