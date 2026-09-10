namespace Ovcuprim.Application.Abstractions;

/// <summary>
/// Caches the taxonomy, which changes rarely and is read on nearly every page. Any admin mutation
/// calls <see cref="Invalidate"/>, so a stale tree is never served after an edit.
/// </summary>
public interface ITaxonomyCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken = default)
        where T : class;

    void Invalidate();
}
