using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>Saved listings: idempotent both ways, scoped to the person who saved them.</summary>
public class FavoriteTests
{
    private static async Task<(ListingTestHarness Harness, Guid ListingId, long ShortId)> BuildAsync()
    {
        var harness = new ListingTestHarness();
        await harness.SeedAsync();
        var id = await ListingLifecycleTests.ActiveListingAsync(harness);
        var shortId = (await harness.ReloadAsync(id)).ShortId;
        harness.ActAsSeller();

        return (harness, id, shortId);
    }

    [Fact]
    public async Task Saving_a_listing_puts_it_in_the_list()
    {
        var (h, id, shortId) = await BuildAsync();
        using var _ = h;

        var added = await h.Favorites.AddAsync(shortId);
        var mine = await h.Favorites.GetMineAsync(new PageRequest());

        Assert.True(added.Succeeded);
        Assert.Single(mine.Value!.Items);
        Assert.Equal(1, mine.Value.Total);
    }

    [Fact]
    public async Task Saving_twice_is_the_same_as_saving_once()
    {
        var (h, id, shortId) = await BuildAsync();
        using var _ = h;

        await h.Favorites.AddAsync(shortId);
        var second = await h.Favorites.AddAsync(shortId);

        Assert.True(second.Succeeded);
        Assert.Equal(1, await h.Db.Favorites.CountAsync());
    }

    [Fact]
    public async Task Removing_something_never_saved_still_succeeds()
    {
        var (h, id, shortId) = await BuildAsync();
        using var _ = h;

        var removed = await h.Favorites.RemoveAsync(shortId);

        Assert.True(removed.Succeeded);
        Assert.Equal(0, await h.Db.Favorites.CountAsync());
    }

    [Fact]
    public async Task Removing_takes_it_back_out()
    {
        var (h, id, shortId) = await BuildAsync();
        using var _ = h;

        await h.Favorites.AddAsync(shortId);
        await h.Favorites.RemoveAsync(shortId);

        var mine = await h.Favorites.GetMineAsync(new PageRequest());

        Assert.Empty(mine.Value!.Items);
    }

    [Fact]
    public async Task Only_a_listing_that_is_actually_on_the_site_can_be_saved()
    {
        var (h, id, shortId) = await BuildAsync();
        using var _ = h;

        await h.Listings.DeleteAsync(id);
        var added = await h.Favorites.AddAsync(shortId);

        Assert.Equal(ResultError.NotFound, added.Error);
    }

    [Fact]
    public async Task One_persons_saved_list_is_invisible_to_another()
    {
        var (h, id, shortId) = await BuildAsync();
        using var _ = h;

        await h.Favorites.AddAsync(shortId);

        h.ActAsOtherUser();
        var theirs = await h.Favorites.GetMineAsync(new PageRequest());

        Assert.Empty(theirs.Value!.Items);
    }

    [Fact]
    public async Task Saving_requires_a_signed_in_visitor()
    {
        var (h, id, shortId) = await BuildAsync();
        using var _ = h;

        h.CurrentUser.UserId = null;

        Assert.Equal(ResultError.Unauthorized, (await h.Favorites.AddAsync(shortId)).Error);
        Assert.Equal(ResultError.Unauthorized, (await h.Favorites.GetMineAsync(new PageRequest())).Error);
    }

    [Fact]
    public async Task A_card_knows_whether_the_current_visitor_saved_it()
    {
        var (h, id, shortId) = await BuildAsync();
        using var _ = h;

        await h.Favorites.AddAsync(shortId);

        var mine = await h.Search.CardsAsync([id]);
        Assert.True(mine[0].IsFavorited);

        h.ActAsOtherUser();
        var theirs = await h.Search.CardsAsync([id]);
        Assert.False(theirs[0].IsFavorited);
    }
}

/// <summary>Reporting a listing, and the queue a moderator works through.</summary>
public class ListingReportTests
{
    private static async Task<(ListingTestHarness Harness, long ShortId)> BuildAsync()
    {
        var harness = new ListingTestHarness();
        await harness.SeedAsync();
        var id = await ListingLifecycleTests.ActiveListingAsync(harness);
        var listing = await harness.ReloadAsync(id);
        harness.ActAsSeller();

        return (harness, listing.ShortId);
    }

    [Fact]
    public async Task A_visitor_can_report_a_live_listing()
    {
        var (h, shortId) = await BuildAsync();
        using var _ = h;

        var result = await h.Reports.SubmitAsync(shortId, new SubmitReportRequest("Prohibited", "Qadağan olunmuş məhsul."));

        Assert.True(result.Succeeded);
        Assert.Equal(1, await h.Db.Reports.CountAsync());
    }

    [Fact]
    public async Task A_signed_out_visitor_can_report_too()
    {
        var (h, shortId) = await BuildAsync();
        using var _ = h;

        h.CurrentUser.UserId = null;
        var result = await h.Reports.SubmitAsync(shortId, new SubmitReportRequest("Fraud", null));

        var report = await h.Db.Reports.SingleAsync();

        Assert.True(result.Succeeded);
        Assert.Null(report.ReporterUserId);
    }

    [Fact]
    public async Task The_same_person_reporting_twice_does_not_inflate_the_queue()
    {
        var (h, shortId) = await BuildAsync();
        using var _ = h;

        await h.Reports.SubmitAsync(shortId, new SubmitReportRequest("Fraud", null));
        await h.Reports.SubmitAsync(shortId, new SubmitReportRequest("Fraud", "yenə"));

        Assert.Equal(1, await h.Db.Reports.CountAsync());
    }

    [Fact]
    public async Task An_unknown_reason_is_a_field_error()
    {
        var (h, shortId) = await BuildAsync();
        using var _ = h;

        var result = await h.Reports.SubmitAsync(shortId, new SubmitReportRequest("Whatever", null));

        Assert.False(result.Succeeded);
        Assert.True(result.FieldErrors!.ContainsKey("reason"));
    }

    [Fact]
    public async Task A_listing_that_is_not_public_answers_like_a_missing_one()
    {
        var (h, shortId) = await BuildAsync();
        using var _ = h;

        // Reporting must not become a way to discover drafts or blocked listings.
        var draft = await h.Listings.CreateDraftAsync(h.NewListing());

        // The in-memory provider does not generate the identity column, so the draft is given a
        // distinct number by hand; on PostgreSQL every listing already has its own.
        var tracked = await h.Db.Listings.FirstAsync(l => l.Id == draft.Value!.Id);
        tracked.ShortId = shortId + 1;
        await h.Db.SaveChangesAsync();

        var onDraft = await h.Reports.SubmitAsync(tracked.ShortId, new SubmitReportRequest("Fraud", null));
        var onMissing = await h.Reports.SubmitAsync(999_999, new SubmitReportRequest("Fraud", null));

        Assert.Equal(ResultError.NotFound, onDraft.Error);
        Assert.Equal(ResultError.NotFound, onMissing.Error);
        Assert.Equal(onMissing.Message, onDraft.Message);
    }

    [Fact]
    public async Task The_queue_shows_open_reports_by_default()
    {
        var (h, shortId) = await BuildAsync();
        using var _ = h;

        await h.Reports.SubmitAsync(shortId, new SubmitReportRequest("Duplicate", null));

        h.ActAsModerator();
        var queue = await h.Reports.GetQueueAsync(null, new PageRequest());
        var item = Assert.Single(queue.Value!.Items);

        Assert.Equal(nameof(ReportReason.Duplicate), item.Reason);
        Assert.Equal(nameof(ReportStatus.Open), item.Status);
        Assert.Equal(shortId, item.ListingShortId);
    }

    [Fact]
    public async Task Resolving_a_report_closes_it_and_leaves_a_trail()
    {
        var (h, shortId) = await BuildAsync();
        using var _ = h;

        await h.Reports.SubmitAsync(shortId, new SubmitReportRequest("Fraud", null));

        h.ActAsModerator();
        var report = await h.Db.Reports.SingleAsync();
        var resolved = await h.Reports.ResolveAsync(report.Id);

        var closed = await h.Db.Reports.SingleAsync();
        var audit = await h.Db.AuditLogs.SingleAsync(a => a.EntityType == nameof(Domain.Entities.Report));

        Assert.True(resolved.Succeeded);
        Assert.Equal(ReportStatus.Resolved, closed.Status);
        Assert.Equal(h.ModeratorId, closed.ResolvedByUserId);
        Assert.Equal("report.resolved", audit.Action);
    }

    [Fact]
    public async Task Dismissing_records_the_other_outcome()
    {
        var (h, shortId) = await BuildAsync();
        using var _ = h;

        await h.Reports.SubmitAsync(shortId, new SubmitReportRequest("Other", null));

        h.ActAsModerator();
        var report = await h.Db.Reports.SingleAsync();
        await h.Reports.DismissAsync(report.Id);

        Assert.Equal(ReportStatus.Dismissed, (await h.Db.Reports.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_closed_report_cannot_be_closed_again()
    {
        var (h, shortId) = await BuildAsync();
        using var _ = h;

        await h.Reports.SubmitAsync(shortId, new SubmitReportRequest("Fraud", null));

        h.ActAsModerator();
        var report = await h.Db.Reports.SingleAsync();
        await h.Reports.ResolveAsync(report.Id);
        var again = await h.Reports.DismissAsync(report.Id);

        Assert.False(again.Succeeded);
        Assert.Equal(ResultError.Conflict, again.Error);
    }
}

/// <summary>The public detail gate the Phase 5 decision introduced.</summary>
public class RestrictedBrowseTests
{
    [Fact]
    public async Task A_restricted_listing_is_browsable_but_flagged_on_the_card()
    {
        using var h = new ListingTestHarness();
        await h.SeedAsync();

        var id = await h.DraftWithImageAsync(h.RestrictedSlug);
        await h.Publishing.PublishAsync(id, new PublishListingRequest(true));
        h.ActAsModerator();
        await h.Moderation.ApproveAsync(id);

        var card = Assert.Single(await h.Search.CardsAsync([id]));

        Assert.True(card.RequiresAgeConfirmation);
    }

    [Fact]
    public async Task Opening_it_needs_an_acknowledgement_first()
    {
        using var h = new ListingTestHarness();
        await h.SeedAsync();

        var id = await h.DraftWithImageAsync(h.RestrictedSlug);
        await h.Publishing.PublishAsync(id, new PublishListingRequest(true));
        h.ActAsModerator();
        await h.Moderation.ApproveAsync(id);

        var shortId = (await h.ReloadAsync(id)).ShortId;

        var gated = await h.Listings.GetPublicAsync(shortId, ageConfirmed: false);
        var opened = await h.Listings.GetPublicAsync(shortId, ageConfirmed: true);

        Assert.False(gated.Succeeded);
        Assert.True(gated.FieldErrors!.ContainsKey("ageConfirmation"));
        Assert.True(opened.Succeeded);
    }

    [Fact]
    public async Task An_ordinary_listing_opens_without_a_gate_and_counts_the_view()
    {
        using var h = new ListingTestHarness();
        await h.SeedAsync();

        var id = await ListingLifecycleTests.ActiveListingAsync(h);
        var shortId = (await h.ReloadAsync(id)).ShortId;

        var opened = await h.Listings.GetPublicAsync(shortId, ageConfirmed: false);

        Assert.True(opened.Succeeded);
        // Buffered rather than written: the request never waits on an UPDATE.
        Assert.Equal(1, h.ViewCounts.Drain()[id]);
    }
}
