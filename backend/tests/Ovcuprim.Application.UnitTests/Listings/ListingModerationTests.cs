using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>
/// Moderation is the only route to the site. These cover the queue, the decisions and the fact
/// that every decision leaves a trail.
/// </summary>
public class ListingModerationTests
{
    private static async Task<ListingTestHarness> BuildAsync()
    {
        var harness = new ListingTestHarness();
        await harness.SeedAsync();
        return harness;
    }

    private static async Task<Guid> PendingAsync(ListingTestHarness h, string? category = null, bool ageConfirmed = false)
    {
        h.ActAsSeller();
        var id = await h.DraftWithImageAsync(category);
        await h.Publishing.PublishAsync(id, new PublishListingRequest(ageConfirmed));
        return id;
    }

    [Fact]
    public async Task The_queue_lists_pending_listings_only()
    {
        using var h = await BuildAsync();
        await PendingAsync(h);
        await h.Listings.CreateDraftAsync(h.NewListing());

        h.ActAsModerator();
        var queue = await h.Moderation.GetQueueAsync(null, new PageRequest());

        Assert.Single(queue.Value!.Items);
    }

    [Fact]
    public async Task A_restricted_category_lands_in_the_strict_queue()
    {
        using var h = await BuildAsync();
        await PendingAsync(h, h.RestrictedSlug, ageConfirmed: true);
        await PendingAsync(h);

        h.ActAsModerator();
        var strict = await h.Moderation.GetQueueAsync(true, new PageRequest());
        var normal = await h.Moderation.GetQueueAsync(false, new PageRequest());

        Assert.Single(strict.Value!.Items);
        Assert.Equal(nameof(RestrictionStatus.Restricted), strict.Value.Items[0].RestrictionStatus);
        Assert.Single(normal.Value!.Items);
    }

    [Fact]
    public async Task An_unclassified_category_queues_strictly_but_keeps_its_own_label()
    {
        using var h = await BuildAsync();

        var category = h.Db.Categories.Single(c => c.Slug == h.RestrictedSlug);
        category.RestrictionStatus = RestrictionStatus.Unclassified;
        await h.Db.SaveChangesAsync();

        await PendingAsync(h, h.RestrictedSlug, ageConfirmed: true);

        h.ActAsModerator();
        var strict = await h.Moderation.GetQueueAsync(true, new PageRequest());
        var item = Assert.Single(strict.Value!.Items);

        Assert.True(item.IsStrict);
        // Never reported as Restricted: the classification is pending, not decided.
        Assert.Equal(nameof(RestrictionStatus.Unclassified), item.RestrictionStatus);
    }

    [Fact]
    public async Task Approval_writes_a_moderation_action_and_an_audit_row()
    {
        using var h = await BuildAsync();
        var id = await PendingAsync(h);

        h.ActAsModerator();
        await h.Moderation.ApproveAsync(id);

        var action = await h.Db.ModerationActions.SingleAsync(a => a.ListingId == id);
        var audit = await h.Db.AuditLogs.SingleAsync(a => a.EntityId == id.ToString());

        Assert.Equal(ModerationActionType.Approved, action.Action);
        Assert.Equal(h.ModeratorId, action.ModeratorUserId);
        Assert.Equal("listing.moderation.approved", audit.Action);
        Assert.Equal(h.ModeratorId, audit.ActorUserId);
    }

    [Fact]
    public async Task Rejection_requires_a_reason_and_shows_it_to_the_seller()
    {
        using var h = await BuildAsync();
        var id = await PendingAsync(h);

        h.ActAsModerator();
        var empty = await h.Moderation.RejectAsync(id, "   ");

        Assert.False(empty.Succeeded);
        Assert.True(empty.FieldErrors!.ContainsKey("reason"));

        await h.Moderation.RejectAsync(id, "Şəkil məhsula aid deyil.");

        h.ActAsSeller();
        var mine = await h.Listings.GetMineAsync("rejected", new PageRequest());
        var card = Assert.Single(mine.Value!.Items);

        Assert.Equal("Şəkil məhsula aid deyil.", card.RejectionReason);
    }

    [Fact]
    public async Task Only_a_pending_listing_can_be_approved()
    {
        using var h = await BuildAsync();
        var id = await PendingAsync(h);

        h.ActAsModerator();
        await h.Moderation.ApproveAsync(id);
        var again = await h.Moderation.ApproveAsync(id);

        Assert.False(again.Succeeded);
        Assert.Equal(ResultError.Conflict, again.Error);
    }

    [Fact]
    public async Task Blocking_hides_a_live_listing_and_unblocking_brings_it_back()
    {
        using var h = await BuildAsync();
        var id = await ListingLifecycleTests.ActiveListingAsync(h);

        h.ActAsModerator();
        var blocked = await h.Moderation.BlockAsync(id, "Qaydalara zidd məzmun.");

        var afterBlock = await h.ReloadAsync(id);
        Assert.True(blocked.Succeeded);
        Assert.Equal(ListingStatus.Blocked, afterBlock.Status);

        var publicView = await h.Listings.GetPublicAsync(afterBlock.ShortId, ageConfirmed: false);
        Assert.Equal(ResultError.NotFound, publicView.Error);

        await h.Moderation.UnblockAsync(id);
        Assert.Equal(ListingStatus.Active, (await h.ReloadAsync(id)).Status);
    }

    [Fact]
    public async Task A_blocked_listing_stays_visible_to_its_seller_with_the_reason()
    {
        using var h = await BuildAsync();
        var id = await ListingLifecycleTests.ActiveListingAsync(h);

        h.ActAsModerator();
        await h.Moderation.BlockAsync(id, "Qaydalara zidd məzmun.");

        h.ActAsSeller();
        var mine = await h.Listings.GetMineAsync("blocked", new PageRequest());
        var card = Assert.Single(mine.Value!.Items);

        Assert.Equal(nameof(ListingStatus.Blocked), card.Status);
        Assert.Equal("Qaydalara zidd məzmun.", card.RejectionReason);

        // Blocked is not folded into the rejected bucket — a seller who cannot resubmit their way
        // out of a block needs to be able to tell the two apart.
        var rejected = await h.Listings.GetMineAsync("rejected", new PageRequest());
        Assert.Empty(rejected.Value!.Items);
    }

    [Fact]
    public async Task The_active_queue_lists_live_listings_and_a_moderator_can_still_block_one()
    {
        using var h = await BuildAsync();
        var activeId = await ListingLifecycleTests.ActiveListingAsync(h);
        await PendingAsync(h);

        h.ActAsModerator();
        var pendingQueue = await h.Moderation.GetQueueAsync(null, new PageRequest());
        var activeQueue = await h.Moderation.GetQueueAsync(null, new PageRequest(), status: "active");

        // The two queues are disjoint: an active listing is not still waiting, and a pending one is
        // not yet live.
        Assert.Single(pendingQueue.Value!.Items);
        var item = Assert.Single(activeQueue.Value!.Items);
        Assert.Equal(activeId, item.Id);

        var blocked = await h.Moderation.BlockAsync(activeId, "Qaydalara zidd məzmun.");
        Assert.True(blocked.Succeeded);
        Assert.Equal(ListingStatus.Blocked, (await h.ReloadAsync(activeId)).Status);

        // Blocking is an ordinary moderation decision: it still writes the append-only trail,
        // alongside the approval already on record from going live in the first place.
        var action = await h.Db.ModerationActions.SingleAsync(
            a => a.ListingId == activeId && a.Action == ModerationActionType.Blocked);
        Assert.Equal(h.ModeratorId, action.ModeratorUserId);
        var audit = await h.Db.AuditLogs.SingleAsync(
            a => a.EntityId == activeId.ToString() && a.Action == "listing.moderation.blocked");
        Assert.Equal(h.ModeratorId, audit.ActorUserId);

        // And the listing left the active queue the same way approval leaves the pending one.
        var afterBlock = await h.Moderation.GetQueueAsync(null, new PageRequest(), status: "active");
        Assert.Empty(afterBlock.Value!.Items);
    }

    [Fact]
    public async Task An_unrecognised_queue_status_is_refused_rather_than_shown_as_pending()
    {
        using var h = await BuildAsync();
        await PendingAsync(h);

        h.ActAsModerator();
        var result = await h.Moderation.GetQueueAsync(null, new PageRequest(), status: "blocked");

        Assert.False(result.Succeeded);
        Assert.True(result.FieldErrors!.ContainsKey("status"));
    }

    [Fact]
    public async Task Blocking_requires_a_reason()
    {
        using var h = await BuildAsync();
        var id = await ListingLifecycleTests.ActiveListingAsync(h);

        h.ActAsModerator();
        var blocked = await h.Moderation.BlockAsync(id, "");

        Assert.False(blocked.Succeeded);
        Assert.True(blocked.FieldErrors!.ContainsKey("reason"));
    }

    [Fact]
    public async Task The_screener_runs_on_every_publish_and_flags_are_kept_for_the_moderator()
    {
        using var h = await BuildAsync();
        h.Screener.Flags.Add(new ScreeningFlag("duplicate", "Oxşar elan mövcuddur."));

        var id = await PendingAsync(h);

        h.ActAsModerator();
        var queue = await h.Moderation.GetQueueAsync(null, new PageRequest());
        var item = Assert.Single(queue.Value!.Items);

        Assert.Equal(1, h.Screener.Calls);
        Assert.Contains("duplicate", item.ScreeningFlags[0], StringComparison.Ordinal);
        // A flag informs the moderator; it never blocks publication by itself.
        Assert.Equal(ListingStatus.PendingModeration, (await h.ReloadAsync(id)).Status);
    }

    [Fact]
    public async Task A_flagged_publish_still_reaches_the_queue()
    {
        using var h = await BuildAsync();
        h.Screener.Flags.Add(new ScreeningFlag("prohibited", "Yoxlama tələb olunur."));

        var id = await PendingAsync(h);

        Assert.Equal(ListingStatus.PendingModeration, (await h.ReloadAsync(id)).Status);
    }
}
