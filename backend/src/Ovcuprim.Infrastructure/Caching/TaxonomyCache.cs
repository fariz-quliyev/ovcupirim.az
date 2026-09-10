using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Ovcuprim.Application.Abstractions;

namespace Ovcuprim.Infrastructure.Caching;

/// <summary>
/// In-memory cache for the taxonomy. Every entry is linked to a single change token, so one
/// <see cref="Invalidate"/> call after an admin mutation clears the whole taxonomy at once and no
/// entry can be left behind holding stale data.
/// </summary>
/// <remarks>
/// This is per-instance. Running more than one API process will need a distributed invalidation
/// signal; that is noted as a scaling item rather than solved here.
/// </remarks>
public sealed class TaxonomyCache(IMemoryCache cache) : ITaxonomyCache, IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly Lock _sync = new();
    private CancellationTokenSource _resetSource = new();

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (cache.TryGetValue(key, out var cached) && cached is T hit)
        {
            return hit;
        }

        var value = await factory(cancellationToken);

        CancellationTokenSource source;

        lock (_sync)
        {
            source = _resetSource;
        }

        cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime
        }.AddExpirationToken(new CancellationChangeToken(source.Token)));

        return value;
    }

    public void Invalidate()
    {
        CancellationTokenSource previous;

        lock (_sync)
        {
            previous = _resetSource;
            _resetSource = new CancellationTokenSource();
        }

        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose()
    {
        _resetSource.Dispose();
    }
}
