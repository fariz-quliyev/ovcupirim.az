using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>
/// The quota mechanism ships complete while the number stays a product decision: with nothing
/// configured every category is unlimited, so an unconfigured environment never blocks a seller.
/// </summary>
public class ListingQuotaTests
{
    private static async Task<ListingTestHarness> BuildAsync(int? limit = null)
    {
        var harness = new ListingTestHarness();
        await harness.SeedAsync();
        harness.QuotaPolicy.Default = limit;
        return harness;
    }

    [Fact]
    public async Task An_unconfigured_quota_means_unlimited()
    {
        using var h = await BuildAsync();

        for (var i = 0; i < 5; i++)
        {
            var id = await h.DraftWithImageAsync();
            var published = await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

            Assert.True(published.Succeeded);
        }

        Assert.Empty(h.Db.ListingQuotas);
    }

    [Fact]
    public async Task A_configured_limit_blocks_the_next_publish()
    {
        using var h = await BuildAsync(limit: 2);

        for (var i = 0; i < 2; i++)
        {
            var allowed = await h.DraftWithImageAsync();
            Assert.True((await h.Publishing.PublishAsync(allowed, new PublishListingRequest(false))).Succeeded);
        }

        var blockedId = await h.DraftWithImageAsync();
        var blocked = await h.Publishing.PublishAsync(blockedId, new PublishListingRequest(false));

        Assert.False(blocked.Succeeded);
        Assert.Equal(ResultError.Conflict, blocked.Error);
    }

    [Fact]
    public async Task Drafting_costs_nothing_and_only_publishing_consumes()
    {
        using var h = await BuildAsync(limit: 1);

        await h.Listings.CreateDraftAsync(h.NewListing());
        await h.Listings.CreateDraftAsync(h.NewListing());

        Assert.Empty(h.Db.ListingQuotas);

        var id = await h.DraftWithImageAsync();
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        Assert.Equal(1, h.Db.ListingQuotas.Single().UsedCount);
    }

    [Fact]
    public async Task The_limit_is_counted_per_category()
    {
        using var h = await BuildAsync();
        h.QuotaPolicy.PerCategory[h.LeafCategoryId] = 1;
        h.QuotaPolicy.PerCategory[h.RestrictedCategoryId] = 1;

        var first = await h.DraftWithImageAsync();
        Assert.True((await h.Publishing.PublishAsync(first, new PublishListingRequest(false))).Succeeded);

        var second = await h.DraftWithImageAsync();
        Assert.False((await h.Publishing.PublishAsync(second, new PublishListingRequest(false))).Succeeded);

        // A different category has its own budget.
        var other = await h.DraftWithImageAsync(h.RestrictedSlug);
        Assert.True((await h.Publishing.PublishAsync(other, new PublishListingRequest(true))).Succeeded);
    }

    [Fact]
    public async Task Restoring_consumes_the_quota_again()
    {
        using var h = await BuildAsync(limit: 2);

        var id = await ListingLifecycleTests.ActiveListingAsync(h);
        h.ActAsSeller();
        await h.Listings.DeleteAsync(id);

        var restored = await h.Publishing.RestoreAsync(id, new PublishListingRequest(false));

        Assert.True(restored.Succeeded);
        Assert.Equal(2, h.Db.ListingQuotas.Single().UsedCount);
    }

    [Fact]
    public async Task Usage_reports_the_next_free_slot_only_once_the_budget_is_gone()
    {
        using var h = await BuildAsync(limit: 2);

        var id = await h.DraftWithImageAsync();
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        var partway = (await h.Quota.GetUsageAsync(h.SellerId)).Single();

        Assert.Equal(1, partway.Used);
        Assert.Equal(2, partway.Limit);
        Assert.Null(partway.NextFreeSlotAt);

        var second = await h.DraftWithImageAsync();
        await h.Publishing.PublishAsync(second, new PublishListingRequest(false));

        var exhausted = (await h.Quota.GetUsageAsync(h.SellerId)).Single();

        Assert.Equal(2, exhausted.Used);
        // Midnight on the 1st in Baku (UTC+4), not in UTC: the moment the seller's own calendar
        // turns over, which is four hours before the same wall-clock date in UTC would.
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(4)), exhausted.NextFreeSlotAt);
    }

    [Fact]
    public async Task A_new_month_starts_a_fresh_budget()
    {
        using var h = await BuildAsync(limit: 1);

        var id = await h.DraftWithImageAsync();
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        h.Clock.Advance(TimeSpan.FromDays(32));

        var next = await h.DraftWithImageAsync();
        var published = await h.Publishing.PublishAsync(next, new PublishListingRequest(false));

        Assert.True(published.Succeeded);
        Assert.Equal(2, h.Db.ListingQuotas.Count());
    }

    [Fact]
    public async Task The_period_turns_over_on_the_Baku_calendar_not_the_UTC_one()
    {
        // 21:00 UTC on 31 August is already 01:00 on 1 September in Baku (UTC+4). A period keyed
        // to the server's UTC clock would still call this August; a seller checking their own
        // calendar would already expect a fresh month.
        using var h = new ListingTestHarness();
        await h.SeedAsync();
        h.QuotaPolicy.Default = 1;
        h.Clock.Set(new DateTimeOffset(2026, 8, 31, 21, 0, 0, TimeSpan.Zero));

        var id = await h.DraftWithImageAsync();
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        var usage = (await h.Quota.GetUsageAsync(h.SellerId)).Single();
        Assert.Equal(1, usage.Used);

        // The period this consumed is September's, in Baku — not August's, in UTC. A publish 40
        // minutes later, still 31 August everywhere in UTC, is already the next Baku month and
        // therefore gets a fresh budget rather than being blocked as a second September submission.
        h.Clock.Advance(TimeSpan.FromMinutes(40));

        var next = await h.DraftWithImageAsync();
        var published = await h.Publishing.PublishAsync(next, new PublishListingRequest(false));

        Assert.False(published.Succeeded, "still the same Baku month (September) — the budget should already be spent");
    }

    [Fact]
    public async Task An_hour_before_the_Baku_boundary_still_counts_against_the_old_month()
    {
        // 19:00 UTC on 31 August is 23:00 in Baku — still August there. The period must not roll
        // over early either.
        using var h = new ListingTestHarness();
        await h.SeedAsync();
        h.QuotaPolicy.Default = 1;
        h.Clock.Set(new DateTimeOffset(2026, 8, 31, 19, 0, 0, TimeSpan.Zero));

        var id = await h.DraftWithImageAsync();
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        // Two hours later — 21:00 UTC, 01:00 in Baku on 1 September — is a real new month and
        // therefore a fresh budget, unlike the same-month case above.
        h.Clock.Advance(TimeSpan.FromHours(2));

        var next = await h.DraftWithImageAsync();
        var published = await h.Publishing.PublishAsync(next, new PublishListingRequest(false));

        Assert.True(published.Succeeded, "already 1 September in Baku — a fresh month, a fresh budget");
        Assert.Equal(2, h.Db.ListingQuotas.Count());
    }

    [Fact]
    public async Task Checking_does_not_consume()
    {
        using var h = await BuildAsync(limit: 1);

        Assert.True((await h.Quota.CheckAsync(h.SellerId, h.LeafCategoryId)).Succeeded);
        Assert.True((await h.Quota.CheckAsync(h.SellerId, h.LeafCategoryId)).Succeeded);
        Assert.Empty(h.Db.ListingQuotas);
    }
}
