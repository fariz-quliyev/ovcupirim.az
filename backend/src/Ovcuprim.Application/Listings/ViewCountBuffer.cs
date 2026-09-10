using System.Collections.Concurrent;

namespace Ovcuprim.Application.Listings;

/// <summary>
/// Collects listing views in memory so a page request never waits on a write.
/// </summary>
/// <remarks>
/// A view is worth counting but not worth an <c>UPDATE</c> per request: on a busy listing that
/// would serialise readers behind a single row. Increments accumulate here and a background pass
/// applies them in one statement. Losing a partial buffer on shutdown costs a handful of views,
/// which is an acceptable trade for keeping reads free of writes.
/// </remarks>
public interface IViewCountBuffer
{
    void Record(Guid listingId);

    /// <summary>Takes everything buffered so far and resets the buffer in one step.</summary>
    IReadOnlyDictionary<Guid, int> Drain();
}

public sealed class ViewCountBuffer : IViewCountBuffer
{
    private ConcurrentDictionary<Guid, int> _counts = new();

    public void Record(Guid listingId) =>
        _counts.AddOrUpdate(listingId, 1, static (_, current) => current + 1);

    public IReadOnlyDictionary<Guid, int> Drain()
    {
        // Swapping the whole dictionary avoids losing increments that land mid-drain.
        var drained = Interlocked.Exchange(ref _counts, new ConcurrentDictionary<Guid, int>());

        return drained;
    }
}
