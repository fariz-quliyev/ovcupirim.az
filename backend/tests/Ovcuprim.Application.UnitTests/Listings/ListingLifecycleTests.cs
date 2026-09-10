using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>
/// The rules a seller feels: what may be edited, when a listing goes back to moderation, and what
/// "delete" actually does. These pin the Tap.az-aligned behaviour agreed for Phase 4.
/// </summary>
public class ListingLifecycleTests
{
    private static async Task<ListingTestHarness> BuildAsync()
    {
        var harness = new ListingTestHarness();
        await harness.SeedAsync();
        return harness;
    }

    [Fact]
    public async Task A_new_listing_starts_as_a_draft_and_is_never_live()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing());

        Assert.True(created.Succeeded);
        Assert.Equal(nameof(ListingStatus.Draft), created.Value!.Status);
        Assert.Null(created.Value.PublishedAt);
        Assert.Null(created.Value.ExpiresAt);
    }

    [Fact]
    public async Task Publishing_always_enters_moderation()
    {
        using var h = await BuildAsync();
        var id = await h.DraftWithImageAsync();

        var published = await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        Assert.True(published.Succeeded);
        Assert.Equal(nameof(ListingStatus.PendingModeration), published.Value!.Status);
        Assert.Null(published.Value.PublishedAt);
    }

    [Fact]
    public async Task Approval_sets_the_thirty_day_window()
    {
        using var h = await BuildAsync();
        var id = await h.DraftWithImageAsync();
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        h.ActAsModerator();
        var approved = await h.Moderation.ApproveAsync(id);

        var listing = await h.ReloadAsync(id);

        Assert.True(approved.Succeeded);
        Assert.Equal(ListingStatus.Active, listing.Status);
        Assert.Equal(h.Clock.UtcNow, listing.PublishedAt);
        Assert.Equal(h.Clock.UtcNow + TimeSpan.FromDays(30), listing.ExpiresAt);
    }

    [Fact]
    public async Task A_published_listing_accepts_a_price_edit_and_returns_to_moderation()
    {
        using var h = await BuildAsync();
        var id = await ActiveListingAsync(h);

        h.ActAsSeller();
        var update = await h.Listings.UpdateAsync(id, UpdateFrom(h, price: 99m));

        Assert.True(update.Succeeded);
        Assert.Equal(nameof(ListingStatus.PendingModeration), update.Value!.Status);
        Assert.Equal(99m, update.Value.Price);
    }

    [Fact]
    public async Task A_published_listing_refuses_a_title_change()
    {
        using var h = await BuildAsync();
        var id = await ActiveListingAsync(h);

        h.ActAsSeller();
        var update = await h.Listings.UpdateAsync(id, UpdateFrom(h, title: "Tamam başqa məhsul"));

        Assert.False(update.Succeeded);
        Assert.Equal(ResultError.Validation, update.Error);
        Assert.True(update.FieldErrors!.ContainsKey("title"));
    }

    [Fact]
    public async Task A_published_listing_refuses_a_region_change()
    {
        using var h = await BuildAsync();
        var id = await ActiveListingAsync(h);

        h.Db.Regions.Add(new Domain.Entities.Region
        {
            Slug = "gence", NameAz = "Gəncə", Type = RegionType.City,
            IsActive = true, IsSelectable = true, SortOrder = 20, CreatedAt = h.Clock.UtcNow
        });
        await h.Db.SaveChangesAsync();

        h.ActAsSeller();
        var update = await h.Listings.UpdateAsync(id, UpdateFrom(h, regionSlug: "gence"));

        Assert.False(update.Succeeded);
        Assert.True(update.FieldErrors!.ContainsKey("regionSlug"));
    }

    [Fact]
    public async Task Hiding_the_phone_on_a_live_listing_does_not_re_enter_moderation()
    {
        using var h = await BuildAsync();
        var id = await ActiveListingAsync(h);

        h.ActAsSeller();
        var update = await h.Listings.UpdateAsync(id, UpdateFrom(h, showPhone: false));

        Assert.True(update.Succeeded);
        Assert.Equal(nameof(ListingStatus.Active), update.Value!.Status);
        Assert.False(update.Value.ShowPhone);
    }

    [Fact]
    public async Task A_draft_can_still_change_everything()
    {
        using var h = await BuildAsync();
        var created = await h.Listings.CreateDraftAsync(h.NewListing());
        var id = created.Value!.Id;

        var update = await h.Listings.UpdateAsync(id, UpdateFrom(h, title: "Yeni başlıq"));

        Assert.True(update.Succeeded);
        Assert.Equal("Yeni başlıq", update.Value!.Title);
        Assert.Equal("yeni-basliq", update.Value.Slug);
    }

    [Fact]
    public async Task Deleting_a_draft_removes_it()
    {
        using var h = await BuildAsync();
        var created = await h.Listings.CreateDraftAsync(h.NewListing());

        var deleted = await h.Listings.DeleteAsync(created.Value!.Id);
        var mine = await h.Listings.GetMineAsync(null, new PageRequest());

        Assert.True(deleted.Succeeded);
        Assert.Empty(mine.Value!.Items);
    }

    [Fact]
    public async Task Deleting_a_live_listing_retires_it_and_keeps_it_restorable()
    {
        using var h = await BuildAsync();
        var id = await ActiveListingAsync(h);

        h.ActAsSeller();
        var deleted = await h.Listings.DeleteAsync(id);
        var listing = await h.ReloadAsync(id);

        Assert.True(deleted.Succeeded);
        Assert.Equal(ListingStatus.Expired, listing.Status);
        Assert.Equal(h.Clock.UtcNow, listing.ExpiresAt);
        Assert.Equal(
            h.Clock.UtcNow + TimeSpan.FromDays(30),
            ListingStateMachine.RestorableUntil(listing.Status, listing.ExpiresAt));
    }

    [Fact]
    public async Task A_retired_listing_can_be_restored_within_thirty_days()
    {
        using var h = await BuildAsync();
        var id = await ActiveListingAsync(h);

        h.ActAsSeller();
        await h.Listings.DeleteAsync(id);

        h.Clock.Advance(TimeSpan.FromDays(29));
        var restored = await h.Publishing.RestoreAsync(id, new PublishListingRequest(false));

        Assert.True(restored.Succeeded);
        Assert.Equal(nameof(ListingStatus.PendingModeration), restored.Value!.Status);
    }

    [Fact]
    public async Task Restoring_after_the_window_is_refused()
    {
        using var h = await BuildAsync();
        var id = await ActiveListingAsync(h);

        h.ActAsSeller();
        await h.Listings.DeleteAsync(id);

        h.Clock.Advance(TimeSpan.FromDays(31));
        var restored = await h.Publishing.RestoreAsync(id, new PublishListingRequest(false));

        Assert.False(restored.Succeeded);
        Assert.Equal(ResultError.Conflict, restored.Error);
    }

    [Fact]
    public async Task An_expired_listing_cannot_be_edited()
    {
        using var h = await BuildAsync();
        var id = await ActiveListingAsync(h);

        h.ActAsSeller();
        await h.Listings.DeleteAsync(id);

        var update = await h.Listings.UpdateAsync(id, UpdateFrom(h));

        Assert.False(update.Succeeded);
        Assert.Equal(ResultError.Conflict, update.Error);
    }

    [Fact]
    public async Task A_rejected_listing_can_be_fixed_and_resubmitted()
    {
        using var h = await BuildAsync();
        var id = await h.DraftWithImageAsync();
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        h.ActAsModerator();
        await h.Moderation.RejectAsync(id, "Şəkil məhsula aid deyil.");

        h.ActAsSeller();
        var fixedUp = await h.Listings.UpdateAsync(id, UpdateFrom(h, title: "Düzəldilmiş başlıq"));
        var resubmitted = await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        Assert.True(fixedUp.Succeeded);
        Assert.True(resubmitted.Succeeded);
        Assert.Equal(nameof(ListingStatus.PendingModeration), resubmitted.Value!.Status);
        Assert.Null(resubmitted.Value.RejectionReason);
    }

    [Fact]
    public async Task Marking_sold_is_available_on_a_live_listing_and_needs_no_moderation()
    {
        using var h = await BuildAsync();
        var id = await ActiveListingAsync(h);

        h.ActAsSeller();
        var sold = await h.Listings.MarkSoldAsync(id);

        Assert.True(sold.Succeeded);
        Assert.Equal(nameof(ListingStatus.Sold), sold.Value!.Status);
    }

    [Fact]
    public async Task A_draft_cannot_be_published_twice()
    {
        using var h = await BuildAsync();
        var id = await h.DraftWithImageAsync();

        var first = await h.Publishing.PublishAsync(id, new PublishListingRequest(false));
        var second = await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(ResultError.Conflict, second.Error);
    }

    [Fact]
    public async Task Publishing_without_an_image_is_refused()
    {
        using var h = await BuildAsync();
        var created = await h.Listings.CreateDraftAsync(h.NewListing());

        var published = await h.Publishing.PublishAsync(created.Value!.Id, new PublishListingRequest(false));

        Assert.False(published.Succeeded);
        Assert.True(published.FieldErrors!.ContainsKey("media"));
    }

    internal static UpdateListingRequest UpdateFrom(
        ListingTestHarness h,
        string? title = null,
        string? regionSlug = null,
        decimal? price = 120m,
        bool showPhone = true) =>
        new(
            regionSlug ?? h.RegionSlug,
            title ?? "İkinəfərlik turist çadırı",
            "Yaxşı vəziyyətdə, az istifadə olunub.",
            price,
            "Used",
            "Naturehike",
            true,
            "0501112233",
            showPhone,
            ListingTestHarness.Attributes(("capacity_person", "\"2\"")));

    /// <summary>Draft to live, through moderation, the only way a listing can get there.</summary>
    internal static async Task<Guid> ActiveListingAsync(ListingTestHarness h)
    {
        var id = await h.DraftWithImageAsync();
        await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        h.ActAsModerator();
        await h.Moderation.ApproveAsync(id);

        return id;
    }
}
