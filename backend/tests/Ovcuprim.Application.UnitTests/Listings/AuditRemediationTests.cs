using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Regions;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>
/// H-1. Editing a live listing sends it back through moderation, and the second approval must not
/// hand it a fresh 30 days or push it back to the top of the page. Tap.az states the rule outright:
/// "Elanda düzəliş etməklə onu irəli çəkmiş olmursunuz."
/// </summary>
public class ReApprovalTests
{
    private static async Task<(ListingTestHarness Harness, Guid Id)> LiveAsync()
    {
        var harness = new ListingTestHarness();
        await harness.SeedAsync();
        var id = await ListingLifecycleTests.ActiveListingAsync(harness);

        return (harness, id);
    }

    [Fact]
    public async Task The_first_approval_starts_the_thirty_day_window()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        var listing = await h.ReloadAsync(id);

        Assert.Equal(h.Clock.UtcNow, listing.PublishedAt);
        Assert.Equal(h.Clock.UtcNow, listing.BumpedAt);
        Assert.Equal(h.Clock.UtcNow + TimeSpan.FromDays(30), listing.ExpiresAt);
    }

    [Fact]
    public async Task Re_approval_after_an_edit_leaves_the_expiry_untouched()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        var before = await h.ReloadAsync(id);

        h.Clock.Advance(TimeSpan.FromDays(20));
        h.ActAsSeller();
        await h.Listings.UpdateAsync(id, ListingLifecycleTests.UpdateFrom(h, price: 99m));

        h.ActAsModerator();
        await h.Moderation.ApproveAsync(id);

        var after = await h.ReloadAsync(id);

        Assert.Equal(ListingStatus.Active, after.Status);
        // The clock keeps running from the original approval; editing buys no extra time.
        Assert.Equal(before.ExpiresAt, after.ExpiresAt);
    }

    [Fact]
    public async Task Re_approval_after_an_edit_does_not_promote_the_listing()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        var before = await h.ReloadAsync(id);

        h.Clock.Advance(TimeSpan.FromDays(5));
        h.ActAsSeller();
        await h.Listings.UpdateAsync(id, ListingLifecycleTests.UpdateFrom(h, price: 55m));

        h.ActAsModerator();
        await h.Moderation.ApproveAsync(id);

        var after = await h.ReloadAsync(id);

        Assert.Equal(before.BumpedAt, after.BumpedAt);
        Assert.Equal(before.PublishedAt, after.PublishedAt);
    }

    [Fact]
    public async Task Repeated_edits_cannot_keep_a_listing_alive_forever()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        var originalExpiry = (await h.ReloadAsync(id)).ExpiresAt;

        // The abuse this fix closes: edit, get re-approved, repeat, and never expire.
        for (var round = 0; round < 3; round++)
        {
            h.Clock.Advance(TimeSpan.FromDays(9));
            h.ActAsSeller();
            await h.Listings.UpdateAsync(id, ListingLifecycleTests.UpdateFrom(h, price: 100m + round));

            h.ActAsModerator();
            await h.Moderation.ApproveAsync(id);
        }

        var listing = await h.ReloadAsync(id);

        Assert.Equal(originalExpiry, listing.ExpiresAt);
        Assert.True(listing.ExpiresAt < h.Clock.UtcNow + TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task Restoring_an_expired_listing_does_start_a_fresh_window()
    {
        var (h, id) = await LiveAsync();
        using var _ = h;

        h.ActAsSeller();
        await h.Listings.DeleteAsync(id);

        h.Clock.Advance(TimeSpan.FromDays(10));
        await h.Publishing.RestoreAsync(id, new PublishListingRequest(false));

        h.ActAsModerator();
        await h.Moderation.ApproveAsync(id);

        var listing = await h.ReloadAsync(id);

        // Restore is a renewal, not an edit: it clears the expiry, so approval starts the clock again.
        Assert.Equal(h.Clock.UtcNow + TimeSpan.FromDays(30), listing.ExpiresAt);
        Assert.Equal(h.Clock.UtcNow, listing.BumpedAt);
    }

    [Fact]
    public async Task A_rejected_listing_resubmitted_and_approved_starts_its_window_then()
    {
        using var h = new ListingTestHarness();
        await h.SeedAsync();

        var id = await h.DraftWithImageAsync();
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        h.ActAsModerator();
        await h.Moderation.RejectAsync(id, "Şəkil məhsula aid deyil.");

        h.Clock.Advance(TimeSpan.FromDays(2));
        h.ActAsSeller();
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        h.ActAsModerator();
        await h.Moderation.ApproveAsync(id);

        var listing = await h.ReloadAsync(id);

        // It was never live before, so this is a first approval.
        Assert.Equal(h.Clock.UtcNow, listing.PublishedAt);
        Assert.Equal(h.Clock.UtcNow + TimeSpan.FromDays(30), listing.ExpiresAt);
    }
}

/// <summary>M-5. A place cannot be taken out of the picker while sellers are still using it.</summary>
public class RegionDeactivationGuardTests
{
    private static async Task<(ListingTestHarness Harness, IRegionService Regions)> BuildAsync()
    {
        var harness = new ListingTestHarness();
        await harness.SeedAsync();

        return (harness, harness.Regions);
    }

    [Fact]
    public async Task A_region_with_live_listings_cannot_be_made_unselectable()
    {
        var (h, regions) = await BuildAsync();
        using var _ = h;

        await ListingLifecycleTests.ActiveListingAsync(h);

        var result = await regions.ImportAsync([
            new("Bakı", "baki", null, "City", null, IsSelectable: false, null, null, 10)
        ]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.Skipped);
        Assert.Contains(result.Value.Errors, e => e.Contains("aktiv elan", StringComparison.Ordinal));

        // The region is left exactly as it was rather than half-applied.
        Assert.True((await h.Db.Regions.SingleAsync(r => r.Slug == "baki")).IsSelectable);
    }

    [Fact]
    public async Task A_region_with_no_live_listings_can_still_be_retired()
    {
        var (h, regions) = await BuildAsync();
        using var _ = h;

        var result = await regions.ImportAsync([
            new("Bakı", "baki", null, "City", null, IsSelectable: false, null, null, 10)
        ]);

        Assert.Equal(0, result.Value!.Skipped);
        Assert.False((await h.Db.Regions.SingleAsync(r => r.Slug == "baki")).IsSelectable);
    }

    [Fact]
    public async Task A_listing_that_is_no_longer_live_does_not_hold_a_region_open()
    {
        var (h, regions) = await BuildAsync();
        using var _ = h;

        var id = await ListingLifecycleTests.ActiveListingAsync(h);
        h.ActAsSeller();
        await h.Listings.DeleteAsync(id);

        var result = await regions.ImportAsync([
            new("Bakı", "baki", null, "City", null, IsSelectable: false, null, null, 10)
        ]);

        Assert.Equal(0, result.Value!.Skipped);
    }

    [Fact]
    public async Task Making_a_region_selectable_again_is_never_blocked()
    {
        var (h, regions) = await BuildAsync();
        using var _ = h;

        var result = await regions.ImportAsync([
            new("Nəsimi", "nesimi", null, "District", null, IsSelectable: true, null, null, 20)
        ]);

        Assert.Equal(0, result.Value!.Skipped);
        Assert.True((await h.Db.Regions.SingleAsync(r => r.Slug == "nesimi")).IsSelectable);
    }
}
