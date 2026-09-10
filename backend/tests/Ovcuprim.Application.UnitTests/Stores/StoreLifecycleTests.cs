using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Stores;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Stores;

/// <summary>
/// Application, approval, suspension and rejection — and the rule that a storefront is public only
/// while it is active.
/// </summary>
public class StoreLifecycleTests
{
    private static async Task<StoreTestHarness> BuildAsync()
    {
        var harness = new StoreTestHarness();
        await harness.SeedAsync();
        harness.ActAsSeller();

        return harness;
    }

    [Fact]
    public async Task An_application_starts_awaiting_approval_and_is_not_public()
    {
        using var h = await BuildAsync();

        var applied = await h.Stores.ApplyAsync(StoreTestHarness.Application());

        Assert.True(applied.Succeeded);
        Assert.Equal(nameof(StoreStatus.PendingVerification), applied.Value!.Status);
        Assert.False(applied.Value.IsPublic);

        // Not visible to anyone until an administrator says so.
        Assert.Equal(ResultError.NotFound, (await h.Stores.GetPublicAsync(applied.Value.Slug)).Error);
    }

    [Fact]
    public async Task The_owner_sees_their_own_pending_store()
    {
        using var h = await BuildAsync();
        await h.Stores.ApplyAsync(StoreTestHarness.Application());

        var mine = await h.Stores.GetMineAsync();

        Assert.True(mine.Succeeded);
        Assert.Equal(nameof(StoreStatus.PendingVerification), mine.Value!.Status);
    }

    [Fact]
    public async Task One_account_may_hold_only_one_storefront()
    {
        using var h = await BuildAsync();
        await h.Stores.ApplyAsync(StoreTestHarness.Application());

        var second = await h.Stores.ApplyAsync(StoreTestHarness.Application("İkinci Mağaza"));

        Assert.False(second.Succeeded);
        Assert.Equal(ResultError.Conflict, second.Error);
    }

    [Fact]
    public async Task Approval_makes_it_public()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        var page = await h.Stores.GetPublicAsync(store.Slug);

        Assert.True(page.Succeeded);
        Assert.Equal("Ovçu Dünyası", page.Value!.Name);
        Assert.True(store.IsPublic);
    }

    [Fact]
    public async Task Suspension_hides_the_shopfront()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        h.ActAsAdmin();
        var suspended = await h.Admin.SuspendAsync(store.Id, "Sənədlər təsdiqlənmədi.");

        Assert.True(suspended.Succeeded);
        Assert.Equal(ResultError.NotFound, (await h.Stores.GetPublicAsync(store.Slug)).Error);
        Assert.Null(await h.Stores.GetPublicIdAsync(store.Slug));
    }

    [Fact]
    public async Task Suspension_needs_a_reason_and_reinstatement_brings_it_back()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        h.ActAsAdmin();

        var withoutReason = await h.Admin.SuspendAsync(store.Id, "   ");
        Assert.True(withoutReason.FieldErrors!.ContainsKey("reason"));

        await h.Admin.SuspendAsync(store.Id, "Yoxlama tələb olunur.");
        await h.Admin.ReinstateAsync(store.Id);

        Assert.True((await h.Stores.GetPublicAsync(store.Slug)).Succeeded);
    }

    [Fact]
    public async Task Rejection_withdraws_the_application_so_the_seller_may_apply_again()
    {
        using var h = await BuildAsync();
        var applied = await h.Stores.ApplyAsync(StoreTestHarness.Application());

        h.ActAsAdmin();
        var rejected = await h.Admin.RejectAsync(applied.Value!.Id, "Ad qaydalara uyğun deyil.");

        Assert.True(rejected.Succeeded);
        Assert.Empty(h.Db.Stores);

        // The one-store-per-account rule must not leave the seller permanently blocked.
        h.ActAsSeller();
        var again = await h.Stores.ApplyAsync(StoreTestHarness.Application("Düzəldilmiş Ad"));

        Assert.True(again.Succeeded);
    }

    [Fact]
    public async Task A_rejection_survives_the_deleted_row_in_the_audit_trail()
    {
        using var h = await BuildAsync();
        var applied = await h.Stores.ApplyAsync(StoreTestHarness.Application());

        h.ActAsAdmin();
        await h.Admin.RejectAsync(applied.Value!.Id, "Ad qaydalara uyğun deyil.");

        var audit = await h.Db.AuditLogs.SingleAsync(a => a.Action == "store.rejected");

        Assert.Equal(h.ModeratorId, audit.ActorUserId);
        Assert.Contains("Ovçu Dünyası", audit.PayloadJson!, StringComparison.Ordinal);
        Assert.Contains("Ad qaydalara uyğun deyil.", audit.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejection_requires_a_reason()
    {
        using var h = await BuildAsync();
        var applied = await h.Stores.ApplyAsync(StoreTestHarness.Application());

        h.ActAsAdmin();
        var rejected = await h.Admin.RejectAsync(applied.Value!.Id, "");

        Assert.False(rejected.Succeeded);
        Assert.True(rejected.FieldErrors!.ContainsKey("reason"));
        Assert.Single(h.Db.Stores);
    }

    [Fact]
    public async Task Only_a_pending_application_can_be_rejected()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        h.ActAsAdmin();
        var rejected = await h.Admin.RejectAsync(store.Id, "Gec qalmış qərar.");

        Assert.Equal(ResultError.Conflict, rejected.Error);
    }

    [Fact]
    public async Task Every_decision_leaves_an_audit_row()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        h.ActAsAdmin();
        await h.Admin.SetVerifiedAsync(store.Id, verified: true);
        await h.Admin.SuspendAsync(store.Id, "Yoxlama.");
        await h.Admin.ReinstateAsync(store.Id);

        var actions = await h.Db.AuditLogs.Select(a => a.Action).ToListAsync();

        Assert.Contains("store.approved", actions);
        Assert.Contains("store.verified", actions);
        Assert.Contains("store.suspended", actions);
        Assert.Contains("store.reinstated", actions);
    }

    [Fact]
    public async Task The_queue_shows_applications_awaiting_a_decision_by_default()
    {
        using var h = await BuildAsync();
        await h.Stores.ApplyAsync(StoreTestHarness.Application());

        h.ActAsAdmin();
        var queue = await h.Admin.GetQueueAsync(null, new PageRequest());
        var item = Assert.Single(queue.Value!.Items);

        Assert.Equal(nameof(StoreStatus.PendingVerification), item.Status);
        Assert.Equal("Test Satıcı", item.OwnerName);
    }
}

/// <summary>The slug is derived once and then fixed, so a storefront's address never moves.</summary>
public class StoreSlugTests
{
    private static async Task<StoreTestHarness> BuildAsync()
    {
        var harness = new StoreTestHarness();
        await harness.SeedAsync();
        harness.ActAsSeller();

        return harness;
    }

    [Fact]
    public async Task The_slug_is_transliterated_from_the_name()
    {
        using var h = await BuildAsync();

        var applied = await h.Stores.ApplyAsync(StoreTestHarness.Application("Ovçu Dünyası"));

        Assert.Equal("ovcu-dunyasi", applied.Value!.Slug);
    }

    [Fact]
    public async Task Renaming_the_store_never_moves_its_address()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        await h.Stores.UpdateMineAsync(new UpdateStoreRequest("Tamamilə Yeni Ad", null, null, null));

        var mine = await h.Stores.GetMineAsync();

        Assert.Equal("Tamamilə Yeni Ad", mine.Value!.Name);
        Assert.Equal(store.Slug, mine.Value.Slug);
        Assert.True((await h.Stores.GetPublicAsync(store.Slug)).Succeeded);
    }

    [Fact]
    public async Task A_colliding_name_takes_a_suffix_rather_than_failing()
    {
        using var h = await BuildAsync();
        await h.Stores.ApplyAsync(StoreTestHarness.Application("Ovçu Dünyası"));

        h.ActAsOtherUser();
        var second = await h.Stores.ApplyAsync(StoreTestHarness.Application("Ovçu Dünyası"));

        Assert.True(second.Succeeded);
        Assert.Equal("ovcu-dunyasi-2", second.Value!.Slug);
    }

    [Fact]
    public async Task A_name_that_produces_no_slug_is_refused_with_a_field_error()
    {
        using var h = await BuildAsync();

        var applied = await h.Stores.ApplyAsync(StoreTestHarness.Application("!!! ???"));

        Assert.False(applied.Succeeded);
        Assert.True(applied.FieldErrors!.ContainsKey("name"));
    }
}
