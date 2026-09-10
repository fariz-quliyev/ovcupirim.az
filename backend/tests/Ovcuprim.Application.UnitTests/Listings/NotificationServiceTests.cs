using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Notifications;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>
/// The in-app channel end to end: what a moderation decision actually writes, and the seller-facing
/// read API over it. <c>ListingModerationTests</c> already covers the decisions themselves; these
/// are about the notification each one is supposed to leave behind.
/// </summary>
public class NotificationServiceTests
{
    private static async Task<ListingTestHarness> BuildAsync()
    {
        var h = new ListingTestHarness();
        await h.SeedAsync();
        return h;
    }

    [Fact]
    public async Task Approving_a_listing_notifies_the_seller()
    {
        using var h = await BuildAsync();
        var id = await PendingAsync(h);

        h.ActAsModerator();
        await h.Moderation.ApproveAsync(id);

        h.ActAsSeller();
        var mine = (await h.Notifications.GetMineAsync(new PageRequest())).Value!;
        var notification = Assert.Single(mine.Items);

        Assert.Equal("listing.approved", notification.Type);
        Assert.False(notification.IsRead);
        Assert.Equal(nameof(Domain.Entities.Listing), notification.EntityType);
    }

    [Fact]
    public async Task Rejecting_a_listing_notifies_the_seller_with_the_reason()
    {
        using var h = await BuildAsync();
        var id = await PendingAsync(h);

        h.ActAsModerator();
        await h.Moderation.RejectAsync(id, "Şəkillər kifayət deyil.");

        h.ActAsSeller();
        var mine = (await h.Notifications.GetMineAsync(new PageRequest())).Value!;
        var notification = Assert.Single(mine.Items);

        Assert.Equal("listing.rejected", notification.Type);
        Assert.Contains("Şəkillər kifayət deyil.", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Blocking_a_listing_notifies_the_seller_with_the_reason()
    {
        using var h = await BuildAsync();
        var id = await ListingLifecycleTests.ActiveListingAsync(h);

        h.ActAsModerator();
        await h.Moderation.BlockAsync(id, "Qaydalara zidd məzmun.");

        h.ActAsSeller();
        var mine = (await h.Notifications.GetMineAsync(new PageRequest())).Value!;

        // Approval already sent one notification (ActiveListingAsync goes through approval); block
        // adds a second, distinct one — nothing here overwrites or replaces the first.
        Assert.Equal(2, mine.Total);
        var blocked = mine.Items.Single(n => n.Type == "listing.blocked");
        Assert.Contains("Qaydalara zidd məzmun.", blocked.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unblocking_does_not_notify()
    {
        // Only approve, reject and block are asked for. Unblock is a moderator undoing their own
        // decision, not an outcome the seller was waiting to hear about.
        using var h = await BuildAsync();
        var id = await ListingLifecycleTests.ActiveListingAsync(h);

        h.ActAsModerator();
        await h.Moderation.BlockAsync(id, "Səbəb.");

        h.ActAsSeller();
        var beforeUnblock = (await h.Notifications.GetMineAsync(new PageRequest())).Value!.Total;

        h.ActAsModerator();
        await h.Moderation.UnblockAsync(id);

        h.ActAsSeller();
        var afterUnblock = (await h.Notifications.GetMineAsync(new PageRequest())).Value!.Total;

        Assert.Equal(beforeUnblock, afterUnblock);
    }

    [Fact]
    public async Task Notifications_come_back_newest_first()
    {
        using var h = await BuildAsync();
        var first = await PendingAsync(h);
        var second = await PendingAsync(h);

        h.ActAsModerator();
        await h.Moderation.RejectAsync(first, "Səbəb 1.");
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await h.Moderation.RejectAsync(second, "Səbəb 2.");

        h.ActAsSeller();
        var mine = (await h.Notifications.GetMineAsync(new PageRequest())).Value!;

        Assert.Equal(2, mine.Items.Count);
        Assert.Contains("Səbəb 2.", mine.Items[0].Body, StringComparison.Ordinal);
        Assert.Contains("Səbəb 1.", mine.Items[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_unread_count_only_counts_unread()
    {
        using var h = await BuildAsync();
        var first = await PendingAsync(h);
        var second = await PendingAsync(h);

        h.ActAsModerator();
        await h.Moderation.RejectAsync(first, "Səbəb 1.");
        await h.Moderation.RejectAsync(second, "Səbəb 2.");

        h.ActAsSeller();
        var beforeRead = (await h.Notifications.GetUnreadCountAsync()).Value!;
        Assert.Equal(2, beforeRead.Count);

        var mine = (await h.Notifications.GetMineAsync(new PageRequest())).Value!;
        await h.Notifications.MarkReadAsync(mine.Items[0].Id);

        var afterRead = (await h.Notifications.GetUnreadCountAsync()).Value!;
        Assert.Equal(1, afterRead.Count);
    }

    [Fact]
    public async Task Marking_an_already_read_notification_read_again_succeeds_and_changes_nothing()
    {
        using var h = await BuildAsync();
        var id = await PendingAsync(h);

        h.ActAsModerator();
        await h.Moderation.RejectAsync(id, "Səbəb.");

        h.ActAsSeller();
        var mine = (await h.Notifications.GetMineAsync(new PageRequest())).Value!;
        var notificationId = mine.Items[0].Id;

        Assert.True((await h.Notifications.MarkReadAsync(notificationId)).Succeeded);
        Assert.True((await h.Notifications.MarkReadAsync(notificationId)).Succeeded);

        Assert.Equal(0, (await h.Notifications.GetUnreadCountAsync()).Value!.Count);
    }

    [Fact]
    public async Task A_seller_cannot_mark_another_sellers_notification_read()
    {
        using var h = await BuildAsync();
        var id = await PendingAsync(h);

        h.ActAsModerator();
        await h.Moderation.RejectAsync(id, "Səbəb.");

        h.ActAsSeller();
        var mine = (await h.Notifications.GetMineAsync(new PageRequest())).Value!;
        var notificationId = mine.Items[0].Id;

        h.ActAsOtherUser();
        var result = await h.Notifications.MarkReadAsync(notificationId);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.NotFound, result.Error);
    }

    [Fact]
    public async Task Marking_all_read_clears_the_unread_count_without_changing_the_total()
    {
        using var h = await BuildAsync();
        var first = await PendingAsync(h);
        var second = await PendingAsync(h);

        h.ActAsModerator();
        await h.Moderation.RejectAsync(first, "Səbəb 1.");
        await h.Moderation.RejectAsync(second, "Səbəb 2.");

        h.ActAsSeller();
        await h.Notifications.MarkAllReadAsync();

        Assert.Equal(0, (await h.Notifications.GetUnreadCountAsync()).Value!.Count);
        Assert.Equal(2, (await h.Notifications.GetMineAsync(new PageRequest())).Value!.Total);
        Assert.All((await h.Notifications.GetMineAsync(new PageRequest())).Value!.Items, n => Assert.True(n.IsRead));
    }

    [Fact]
    public async Task Every_read_operation_requires_authentication()
    {
        using var h = await BuildAsync();
        h.CurrentUser.UserId = null;

        Assert.Equal(ResultError.Unauthorized, (await h.Notifications.GetMineAsync(new PageRequest())).Error);
        Assert.Equal(ResultError.Unauthorized, (await h.Notifications.GetUnreadCountAsync()).Error);
        Assert.Equal(ResultError.Unauthorized, (await h.Notifications.MarkReadAsync(Guid.NewGuid())).Error);
        Assert.Equal(ResultError.Unauthorized, (await h.Notifications.MarkAllReadAsync()).Error);
    }

    private static async Task<Guid> PendingAsync(ListingTestHarness h)
    {
        h.ActAsSeller();
        var id = await h.DraftWithImageAsync();
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));
        return id;
    }
}
