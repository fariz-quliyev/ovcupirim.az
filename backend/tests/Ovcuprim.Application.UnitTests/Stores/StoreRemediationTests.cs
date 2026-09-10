using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Stores;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Stores;

/// <summary>
/// M-1 and L-1: the moderation verbs each own exactly one source state, so the audit trail says
/// what actually happened rather than what the caller happened to click.
/// </summary>
public class StoreModerationGuardTests
{
    private static async Task<StoreTestHarness> BuildAsync()
    {
        var harness = new StoreTestHarness();
        await harness.SeedAsync();

        return harness;
    }

    [Fact]
    public async Task Approve_takes_a_pending_application_live()
    {
        using var h = await BuildAsync();
        h.ActAsSeller();
        var applied = await h.Stores.ApplyAsync(StoreTestHarness.Application());

        h.ActAsAdmin();
        var approved = await h.Admin.ApproveAsync(applied.Value!.Id);

        Assert.True(approved.Succeeded);
        Assert.Equal(StoreStatus.Active, (await h.Db.Stores.FindAsync(applied.Value.Id))!.Status);
    }

    [Fact]
    public async Task Approve_will_not_un_suspend_a_storefront()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        h.ActAsAdmin();
        await h.Admin.SuspendAsync(store.Id, "Yoxlama tələb olunur.");

        var approved = await h.Admin.ApproveAsync(store.Id);

        // A storefront taken down for cause comes back one way only.
        Assert.Equal(ResultError.Conflict, approved.Error);
        Assert.Equal(StoreStatus.Suspended, (await h.Db.Stores.FindAsync(store.Id))!.Status);
    }

    [Fact]
    public async Task A_suspended_storefront_still_comes_back_through_reinstate()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        h.ActAsAdmin();
        await h.Admin.SuspendAsync(store.Id, "Yoxlama tələb olunur.");

        Assert.True((await h.Admin.ReinstateAsync(store.Id)).Succeeded);
        Assert.Equal(StoreStatus.Active, (await h.Db.Stores.FindAsync(store.Id))!.Status);
    }

    [Fact]
    public async Task A_reinstatement_is_never_recorded_as_an_approval()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        h.ActAsAdmin();
        await h.Admin.SuspendAsync(store.Id, "Yoxlama.");
        await h.Admin.ApproveAsync(store.Id);
        await h.Admin.ReinstateAsync(store.Id);

        var actions = await h.Db.AuditLogs.Select(a => a.Action).ToListAsync();

        // The refused approve wrote nothing; only the two real decisions are in the trail, plus
        // the original approval that opened the shop.
        Assert.Equal(3, actions.Count);
        Assert.Single(actions, a => a == "store.reinstated");
        Assert.Single(actions, a => a == "store.approved");
        Assert.Single(actions, a => a == "store.suspended");
    }

    [Fact]
    public async Task An_already_active_storefront_cannot_be_approved_twice()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        h.ActAsAdmin();

        Assert.Equal(ResultError.Conflict, (await h.Admin.ApproveAsync(store.Id)).Error);
    }

    [Fact]
    public async Task A_badge_cannot_be_granted_to_an_application_nobody_has_approved()
    {
        using var h = await BuildAsync();
        h.ActAsSeller();
        var applied = await h.Stores.ApplyAsync(StoreTestHarness.Application());

        h.ActAsAdmin();
        var verified = await h.Admin.SetVerifiedAsync(applied.Value!.Id, verified: true);

        Assert.Equal(ResultError.Conflict, verified.Error);
        Assert.False((await h.Db.Stores.FindAsync(applied.Value.Id))!.IsVerified);
    }

    [Fact]
    public async Task A_badge_can_be_granted_to_an_active_storefront_and_taken_away_at_any_time()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        h.ActAsAdmin();

        Assert.True((await h.Admin.SetVerifiedAsync(store.Id, verified: true)).Succeeded);
        await h.Admin.SuspendAsync(store.Id, "Yoxlama.");

        // Removing a badge is never blocked: it is the direction that takes something away.
        Assert.True((await h.Admin.SetVerifiedAsync(store.Id, verified: false)).Succeeded);
        Assert.False((await h.Db.Stores.FindAsync(store.Id))!.IsVerified);
    }

    [Fact]
    public async Task The_queue_refuses_a_status_it_does_not_recognise()
    {
        using var h = await BuildAsync();
        h.ActAsSeller();
        await h.Stores.ApplyAsync(StoreTestHarness.Application());

        h.ActAsAdmin();
        var queue = await h.Admin.GetQueueAsync("Rejected", new PageRequest());

        // Silently showing a different queue than the one asked for is how a moderator comes to
        // believe a queue is empty.
        Assert.False(queue.Succeeded);
        Assert.True(queue.FieldErrors!.ContainsKey("status"));
    }

    [Fact]
    public async Task The_queue_still_defaults_to_applications_awaiting_a_decision()
    {
        using var h = await BuildAsync();
        h.ActAsSeller();
        await h.Stores.ApplyAsync(StoreTestHarness.Application());

        h.ActAsAdmin();

        Assert.Single((await h.Admin.GetQueueAsync(null, new PageRequest())).Value!.Items);
        Assert.Single((await h.Admin.GetQueueAsync("  ", new PageRequest())).Value!.Items);
        Assert.Single((await h.Admin.GetQueueAsync("pendingverification", new PageRequest())).Value!.Items);
    }
}

/// <summary>
/// M-2: a storefront edited from two places at once. The image writes now answer the way the store
/// editor does, and nothing is left behind outside the orphan sweep's reach.
/// </summary>
public class StoreMediaConcurrencyTests
{
    private static async Task<(StoreTestHarness Harness, StoreOwnerDto Store)> BuildAsync()
    {
        var harness = new StoreTestHarness();
        await harness.SeedAsync();
        var store = await harness.ActiveStoreAsync();

        return (harness, store);
    }

    /// <summary>
    /// Advances the row's concurrency token behind the service's back, which is what a competing
    /// edit in another tab amounts to.
    /// </summary>
    private static async Task CompetingEditAsync(StoreTestHarness h, Guid storeId)
    {
        await using var other = h.NewContext();

        var store = await other.Stores.FirstAsync(s => s.Id == storeId);
        store.Description = $"Başqa yerdə dəyişdirildi {Guid.NewGuid():N}";

        await other.SaveChangesAsync();
    }

    [Fact]
    public async Task A_lost_race_on_upload_is_a_conflict_rather_than_a_crash()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        // The service loads the store, then someone else saves before the upload commits.
        await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());
        await CompetingEditAsync(h, store.Id);

        var second = await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());

        Assert.Equal(ResultError.Conflict, second.Error);
    }

    [Fact]
    public async Task A_lost_race_leaves_no_file_the_sweep_cannot_reach()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());
        var committed = h.Storage.Objects.Keys.Single();

        await CompetingEditAsync(h, store.Id);
        await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());

        // The file written for the failed attempt is removed outright, and the one the store still
        // points at is untouched. Either way every key sits under the swept prefix.
        Assert.Equal(committed, Assert.Single(h.Storage.Objects.Keys));
        Assert.All(h.Storage.Objects.Keys, key => Assert.StartsWith("stores/", key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lost_race_on_removal_keeps_the_image_the_store_still_points_at()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());
        var committed = h.Storage.Objects.Keys.Single();

        await CompetingEditAsync(h, store.Id);
        var removed = await h.Media.RemoveAsync(StoreImageKind.Logo);

        Assert.Equal(ResultError.Conflict, removed.Error);
        Assert.Contains(committed, h.Storage.Objects.Keys);
    }

    [Fact]
    public async Task An_uncontended_upload_still_succeeds_and_moves_the_token()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        var before = (await h.Db.Stores.FindAsync(store.Id))!.Version;
        var result = await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());

        Assert.True(result.Succeeded);

        // An image is content, so it advances the token like any other edit — what must not move it
        // is a counter, and that is covered separately.
        Assert.True((await h.Db.Stores.FindAsync(store.Id))!.Version > before);
    }
}
