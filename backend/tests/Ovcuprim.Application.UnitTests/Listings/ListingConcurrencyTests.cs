using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>
/// M-1. The listing's concurrency token is an application-managed <c>Version</c> rather than
/// PostgreSQL's <c>xmin</c>, so that counting a view cannot make a seller's open edit look stale.
/// </summary>
/// <remarks>
/// These run on the in-memory provider, which the previous xmin mapping could not — the conflict
/// path had no automated coverage at all before this change. The counter statements themselves are
/// raw SQL and are verified against a real database.
/// </remarks>
public class ListingConcurrencyTests
{
    private static async Task<(ListingTestHarness Harness, Guid Id)> LiveAsync()
    {
        var harness = new ListingTestHarness();
        await harness.SeedAsync();
        var id = await ListingLifecycleTests.ActiveListingAsync(harness);
        harness.ActAsSeller();

        return (harness, id);
    }

    [Fact]
    public async Task A_new_listing_starts_at_version_zero()
    {
        using var h = new ListingTestHarness();
        await h.SeedAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing());

        Assert.Equal(0, (await h.ReloadAsync(created.Value!.Id)).Version);
    }

    [Fact]
    public async Task Editing_the_content_moves_the_version_on()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        var before = (await h.ReloadAsync(id)).Version;

        await h.Listings.UpdateAsync(id, ListingLifecycleTests.UpdateFrom(h, price: 77m));

        var after = (await h.ReloadAsync(id)).Version;

        Assert.True(after > before, $"Expected the version to advance from {before}, saw {after}.");
    }

    [Fact]
    public async Task Every_content_change_advances_it_again()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        var start = (await h.ReloadAsync(id)).Version;

        for (var price = 10; price < 40; price += 10)
        {
            h.ActAsSeller();
            await h.Listings.UpdateAsync(id, ListingLifecycleTests.UpdateFrom(h, price: price));

            h.ActAsModerator();
            await h.Moderation.ApproveAsync(id);
        }

        Assert.True((await h.ReloadAsync(id)).Version >= start + 3);
    }

    [Fact]
    public async Task A_stale_write_is_refused_rather_than_silently_overwriting()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        // The seller's context reads the listing and holds it, as an open edit form does.
        var held = await h.Db.Listings.FirstAsync(l => l.Id == id);

        // Meanwhile the same listing is changed elsewhere and the version moves on.
        await using (var other = h.NewContext())
        {
            var theirs = await other.Listings.FirstAsync(l => l.Id == id);
            theirs.Description = "Başqa yerdə edilmiş dəyişiklik.";
            await other.SaveChangesAsync();
        }

        held.Description = "Satıcının köhnə formadan göndərdiyi mətn.";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => h.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_service_turns_a_stale_write_into_a_conflict()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        // Tracked by the service's own context, so the update it performs carries this original
        // version — exactly the state a seller's request is in when the form was loaded earlier.
        await h.Db.Listings.FirstAsync(l => l.Id == id);

        await using (var other = h.NewContext())
        {
            var theirs = await other.Listings.FirstAsync(l => l.Id == id);
            theirs.Description = "Başqa yerdə edilmiş dəyişiklik.";
            await other.SaveChangesAsync();
        }

        var result = await h.Listings.UpdateAsync(id, ListingLifecycleTests.UpdateFrom(h, price: 42m));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Conflict, result.Error);
        Assert.Contains("yeniləyib", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_concurrent_edits_leave_exactly_one_winner()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        await using var first = h.NewContext();
        await using var second = h.NewContext();

        // Both read the same version before either writes.
        var mine = await first.Listings.FirstAsync(l => l.Id == id);
        var theirs = await second.Listings.FirstAsync(l => l.Id == id);

        mine.Description = "Birinci redaktə.";
        theirs.Description = "İkinci redaktə.";

        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        // The winner's text is what survived; nothing was silently lost.
        Assert.Equal("Birinci redaktə.", (await h.ReloadAsync(id)).Description);
    }

    [Fact]
    public async Task Marking_a_listing_sold_also_moves_the_version()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        var before = (await h.ReloadAsync(id)).Version;
        await h.Listings.MarkSoldAsync(id);

        var listing = await h.ReloadAsync(id);

        Assert.Equal(ListingStatus.Sold, listing.Status);
        Assert.True(listing.Version > before);
    }

    [Fact]
    public async Task Reading_a_listing_never_moves_the_version()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        var before = (await h.ReloadAsync(id)).Version;
        var shortId = (await h.ReloadAsync(id)).ShortId;

        await h.Listings.GetAsync(id);
        await h.Listings.GetPublicAsync(shortId, ageConfirmed: false);
        await h.Listings.GetMineAsync(null, new PageRequest());

        Assert.Equal(before, (await h.ReloadAsync(id)).Version);
    }

    [Fact]
    public async Task Buffering_a_view_never_moves_the_version()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        var before = (await h.ReloadAsync(id)).Version;
        var shortId = (await h.ReloadAsync(id)).ShortId;

        for (var i = 0; i < 5; i++)
        {
            await h.Listings.GetPublicAsync(shortId, ageConfirmed: false);
        }

        // Views accumulate off to the side; the listing row is not written at all.
        Assert.Equal(5, h.ViewCounts.Drain()[id]);
        Assert.Equal(before, (await h.ReloadAsync(id)).Version);
    }

    [Fact]
    public async Task Saving_and_unsaving_a_listing_never_moves_the_version()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        var shortId = (await h.ReloadAsync(id)).ShortId;
        var before = (await h.ReloadAsync(id)).Version;

        await h.Favorites.AddAsync(shortId);
        await h.Favorites.RemoveAsync(shortId);

        // FavoriteCount is recomputed by the maintenance pass, not written on the toggle.
        Assert.Equal(before, (await h.ReloadAsync(id)).Version);
    }
}
