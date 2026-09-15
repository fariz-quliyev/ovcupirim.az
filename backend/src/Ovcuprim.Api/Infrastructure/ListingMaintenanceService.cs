using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Listings.Search;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Api.Infrastructure;

/// <summary>
/// The site's housekeeping pass: retires listings whose 30 days are up, clears drafts that were
/// abandoned long enough that their images are just storage cost, refreshes the denormalised
/// listing counts the category tree and region picker show, and applies buffered view counts.
/// Runs in the host rather than in Infrastructure so no hosting package has to be added to a
/// class library.
/// </summary>
public sealed class ListingMaintenanceService(
    IServiceScopeFactory scopeFactory,
    ILogger<ListingMaintenanceService> logger) : BackgroundService
{
    /// <summary>
    /// Paced by the most frequent job. Expiry and abandoned-draft sweeps are hourly, the facet
    /// counts quarter-hourly, and the view flush every pass.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    private static readonly TimeSpan CountInterval = TimeSpan.FromMinutes(15);

    private DateTimeOffset _lastSweep = DateTimeOffset.MinValue;

    private DateTimeOffset _lastCountRefresh = DateTimeOffset.MinValue;

    /// <summary>Matches the restore window: an untouched draft is swept once it is clearly abandoned.</summary>
    private static readonly TimeSpan AbandonedDraftAge = TimeSpan.FromDays(30);

    /// <summary>Everything listing media is written under.</summary>
    private const string MediaPrefix = "listings";

    /// <summary>Storefront logos and banners.</summary>
    private const string StoreMediaPrefix = "stores";

    /// <summary>
    /// Category pictures. Swept like the others — and, like a storefront's, referenced from a
    /// column rather than a media row, so the reconciliation below has to collect those keys
    /// explicitly. Adding the prefix without adding the references would delete every category
    /// picture on the site the first time this ran.
    /// </summary>
    private const string CategoryMediaPrefix = "categories";

    /// <summary>
    /// How old a stored object must be before it can be considered orphaned. Far longer than any
    /// single upload request, so a file written moments before its row is never mistaken for one.
    /// </summary>
    private static readonly TimeSpan OrphanGracePeriod = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Listing maintenance pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var store = scope.ServiceProvider.GetRequiredService<IListingSearchStore>();
        var views = scope.ServiceProvider.GetRequiredService<IViewCountBuffer>();
        var now = clock.UtcNow;

        // Every pass: drain whatever page views accumulated since the last one.
        var drained = views.Drain();

        if (drained.Count > 0)
        {
            await store.ApplyViewCountsAsync(drained, cancellationToken);
        }

        if (now - _lastCountRefresh >= CountInterval)
        {
            _lastCountRefresh = now;
            var changed = await store.RefreshListingCountsAsync(cancellationToken);

            if (changed > 0)
            {
                logger.LogInformation("Listing counts refreshed on {Rows} rows.", changed);
            }
        }

        if (now - _lastSweep < SweepInterval)
        {
            return;
        }

        _lastSweep = now;

        var expired = await db.Listings
            .Where(l => l.Status == ListingStatus.Active && l.ExpiresAt != null && l.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var listing in expired)
        {
            listing.Status = ListingStatus.Expired;
        }

        var cutoff = now - AbandonedDraftAge;

        // IgnoreQueryFilters is load-bearing: a draft the seller deleted has DeletedAt set, which
        // the global filter hides, so without this its images stay on disk forever.
        var abandoned = await db.Listings
            .IgnoreQueryFilters()
            .Include(l => l.Media)
            .Where(l => l.Status == ListingStatus.Draft && (l.UpdatedAt ?? l.CreatedAt) <= cutoff)
            .ToListAsync(cancellationToken);

        var orphanKeys = new List<string>();

        foreach (var draft in abandoned)
        {
            orphanKeys.AddRange(draft.Media.SelectMany(m => m.Variants.Values.Append(m.StorageKey)));

            // Already-removed drafts are here only for their images; their timestamp stands.
            draft.DeletedAt ??= now;
        }

        if (expired.Count > 0 || abandoned.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var key in orphanKeys)
        {
            await storage.DeleteAsync(key, cancellationToken);
        }

        var reconciled = await ReconcileOrphanedStorageAsync(db, storage, now, cancellationToken);

        if (expired.Count > 0 || abandoned.Count > 0 || reconciled > 0)
        {
            logger.LogInformation(
                "Listing maintenance: {Expired} expired, {Abandoned} abandoned drafts removed, {Orphans} orphaned objects reconciled.",
                expired.Count,
                abandoned.Count,
                reconciled);
        }
    }

    /// <summary>
    /// Deletes stored objects that no <see cref="Ovcuprim.Domain.Entities.ListingMedia"/> row
    /// refers to any more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Removing an image deletes its row first and its files second, so a failure between the two
    /// strands the files with nothing pointing at them. This is the pass that catches those.
    /// </para>
    /// <para>
    /// Two things keep it safe. Only objects older than <see cref="OrphanGracePeriod"/> are
    /// considered, so an upload that has written its files but not yet committed its row is never
    /// touched. And the referenced set is read with the query filters off, so media belonging to a
    /// soft-deleted listing still counts as referenced rather than being swept out from under a
    /// listing an administrator may restore.
    /// </para>
    /// </remarks>
    private static async Task<int> ReconcileOrphanedStorageAsync(
        IAppDbContext db, IFileStorage storage, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - OrphanGracePeriod;

        var candidates = (await storage.ListKeysAsync(MediaPrefix, cutoff, cancellationToken))
            .Concat(await storage.ListKeysAsync(StoreMediaPrefix, cutoff, cancellationToken))
            .Concat(await storage.ListKeysAsync(CategoryMediaPrefix, cutoff, cancellationToken))
            .ToList();

        if (candidates.Count == 0)
        {
            return 0;
        }

        var rows = await db.ListingMedia
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(m => new { m.StorageKey, m.Variants })
            .ToListAsync(cancellationToken);

        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            referenced.Add(row.StorageKey);

            foreach (var variant in row.Variants.Values)
            {
                referenced.Add(variant);
            }
        }

        // A storefront's images live in columns rather than media rows, so they are collected
        // separately — but they are just as referenced.
        var storeImages = await db.Stores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(s => new { s.LogoStorageKey, s.BannerStorageKey })
            .ToListAsync(cancellationToken);

        foreach (var store in storeImages)
        {
            if (store.LogoStorageKey is not null)
            {
                referenced.Add(store.LogoStorageKey);
            }

            if (store.BannerStorageKey is not null)
            {
                referenced.Add(store.BannerStorageKey);
            }
        }

        // Category pictures, likewise held in a column. Query filters off for the same reason: a
        // deactivated category still owns its picture and may be brought back.
        var categoryImages = await db.Categories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.ImageKey != null)
            .Select(c => c.ImageKey!)
            .ToListAsync(cancellationToken);

        foreach (var key in categoryImages)
        {
            referenced.Add(key);
        }

        var removed = 0;

        foreach (var key in candidates.Where(key => !referenced.Contains(key)))
        {
            await storage.DeleteAsync(key, cancellationToken);
            removed++;
        }

        return removed;
    }
}
