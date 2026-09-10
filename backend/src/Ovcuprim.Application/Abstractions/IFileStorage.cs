namespace Ovcuprim.Application.Abstractions;

/// <summary>Media storage behind an interface: local disk in development, S3-compatible in production.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken cancellationToken = default);

    Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Public URL a browser can fetch the object from.</summary>
    string GetPublicUrl(string key);

    /// <summary>
    /// Keys under <paramref name="prefix"/> last written before <paramref name="modifiedBefore"/>.
    /// </summary>
    /// <remarks>
    /// The age bound is the safety mechanism, not an optimisation: an upload writes its object a
    /// moment before the database row exists, so a reconciliation sweep that considered fresh
    /// objects would delete images out from under a listing being created. Callers pass a cutoff
    /// comfortably older than any single request.
    /// </remarks>
    Task<IReadOnlyList<string>> ListKeysAsync(
        string prefix, DateTimeOffset modifiedBefore, CancellationToken cancellationToken = default);
}
