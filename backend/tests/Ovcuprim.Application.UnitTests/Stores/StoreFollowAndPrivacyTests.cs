using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Stores;

namespace Ovcuprim.Application.UnitTests.Stores;

/// <summary>"İzləyici ol" — idempotent both ways, and scoped to the person who followed.</summary>
public class StoreFollowTests
{
    private static async Task<(StoreTestHarness Harness, StoreOwnerDto Store)> BuildAsync()
    {
        var harness = new StoreTestHarness();
        await harness.SeedAsync();
        var store = await harness.ActiveStoreAsync();

        return (harness, store);
    }

    [Fact]
    public async Task Following_shows_up_on_the_storefront_for_that_visitor_only()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        await h.Follows.FollowAsync(store.Slug);

        Assert.True((await h.Stores.GetPublicAsync(store.Slug)).Value!.IsFollowing);

        h.ActAsOtherUser();
        Assert.False((await h.Stores.GetPublicAsync(store.Slug)).Value!.IsFollowing);
    }

    [Fact]
    public async Task Following_twice_is_the_same_as_following_once()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        await h.Follows.FollowAsync(store.Slug);
        var second = await h.Follows.FollowAsync(store.Slug);

        Assert.True(second.Succeeded);
        Assert.Single(h.Db.StoreFollows);
    }

    [Fact]
    public async Task Unfollowing_something_never_followed_still_succeeds()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        Assert.True((await h.Follows.UnfollowAsync(store.Slug)).Succeeded);
        Assert.Empty(h.Db.StoreFollows);
    }

    [Fact]
    public async Task A_storefront_that_is_not_public_cannot_be_followed()
    {
        using var h = new StoreTestHarness();
        await h.SeedAsync();
        h.ActAsSeller();

        var applied = await h.Stores.ApplyAsync(StoreTestHarness.Application());

        var followed = await h.Follows.FollowAsync(applied.Value!.Slug);

        Assert.Equal(ResultError.NotFound, followed.Error);
    }

    [Fact]
    public async Task Following_requires_a_signed_in_visitor()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        h.CurrentUser.UserId = null;

        Assert.Equal(ResultError.Unauthorized, (await h.Follows.FollowAsync(store.Slug)).Error);
        Assert.Equal(ResultError.Unauthorized, (await h.Follows.GetMineAsync(new PageRequest())).Error);
    }

    [Fact]
    public async Task A_suspended_store_drops_out_of_the_followed_list()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        await h.Follows.FollowAsync(store.Slug);
        Assert.Single((await h.Follows.GetMineAsync(new PageRequest())).Value!.Items);

        h.ActAsAdmin();
        await h.Admin.SuspendAsync(store.Id, "Yoxlama.");

        h.ActAsSeller();
        Assert.Empty((await h.Follows.GetMineAsync(new PageRequest())).Value!.Items);
    }

    [Fact]
    public async Task Following_never_moves_the_stores_concurrency_token()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        var before = (await h.Db.Stores.FindAsync(store.Id))!.Version;

        await h.Follows.FollowAsync(store.Slug);
        await h.Follows.UnfollowAsync(store.Slug);

        // The counter is recomputed by the maintenance pass, so a follow cannot invalidate an edit
        // the owner already has open.
        Assert.Equal(before, (await h.Db.Stores.FindAsync(store.Id))!.Version);
    }

    [Fact]
    public async Task Editing_the_store_does_move_its_token()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        var before = (await h.Db.Stores.FindAsync(store.Id))!.Version;

        await h.Stores.UpdateMineAsync(new UpdateStoreRequest("Yeni Ad", null, null, null));

        Assert.True((await h.Db.Stores.FindAsync(store.Id))!.Version > before);
    }
}

/// <summary>What a storefront may publish about its owner, and what it may not.</summary>
public class StorePrivacyTests
{
    private static async Task<(StoreTestHarness Harness, StoreOwnerDto Store)> BuildAsync()
    {
        var harness = new StoreTestHarness();
        await harness.SeedAsync();
        var store = await harness.ActiveStoreAsync();

        return (harness, store);
    }

    [Fact]
    public async Task The_public_page_never_carries_the_owners_account_phone()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        var page = await h.Stores.GetPublicAsync(store.Slug);
        var serialised = System.Text.Json.JsonSerializer.Serialize(page.Value);

        // The seller's account number, set up by the harness, is a different field entirely.
        Assert.DoesNotContain("+994501112233", serialised, StringComparison.Ordinal);
        Assert.DoesNotContain("OwnerUserId", serialised, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_store_phone_is_masked_until_it_is_asked_for()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        var page = await h.Stores.GetPublicAsync(store.Slug);

        Assert.True(page.Value!.ShowPhone);
        Assert.DoesNotContain("1112233", page.Value.PhoneMasked!, StringComparison.Ordinal);

        var revealed = await h.Stores.GetPublicPhoneAsync(store.Slug);

        Assert.Equal("+994501112233", revealed.Value!.Phone);
    }

    [Fact]
    public async Task A_store_with_no_phone_offers_nothing_to_reveal()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        await h.Stores.UpdateMineAsync(new UpdateStoreRequest("Ovçu Dünyası", null, null, null));

        var page = await h.Stores.GetPublicAsync(store.Slug);

        Assert.False(page.Value!.ShowPhone);
        Assert.Null(page.Value.PhoneMasked);
        Assert.Equal(ResultError.NotFound, (await h.Stores.GetPublicPhoneAsync(store.Slug)).Error);
    }

    [Fact]
    public async Task A_suspended_store_reveals_no_phone_number()
    {
        var (h, store) = await BuildAsync();
        using var _ = h;

        h.ActAsAdmin();
        await h.Admin.SuspendAsync(store.Id, "Yoxlama.");

        Assert.Equal(ResultError.NotFound, (await h.Stores.GetPublicPhoneAsync(store.Slug)).Error);
    }

    [Fact]
    public async Task An_invalid_store_phone_is_refused_rather_than_stored()
    {
        using var h = new StoreTestHarness();
        await h.SeedAsync();
        h.ActAsSeller();

        var applied = await h.Stores.ApplyAsync(
            new ApplyForStoreRequest("Ovçu Dünyası", null, null, "12345"));

        Assert.False(applied.Succeeded);
        Assert.True(applied.FieldErrors!.ContainsKey("phone"));
    }
}

/// <summary>Logo and banner go through the same pipeline listing images do.</summary>
public class StoreMediaTests
{
    private static async Task<(StoreTestHarness Harness, StoreOwnerDto Store)> BuildAsync()
    {
        var harness = new StoreTestHarness();
        await harness.SeedAsync();
        var store = await harness.ActiveStoreAsync();

        return (harness, store);
    }

    [Fact]
    public async Task A_logo_is_re_encoded_and_stored_under_a_random_key()
    {
        var (h, _) = await BuildAsync();
        using var _h = h;

        var result = await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value!.LogoUrl);
        Assert.Single(h.Storage.Objects);
        Assert.All(h.Storage.Objects.Keys, key => Assert.StartsWith("stores/", key, StringComparison.Ordinal));
        Assert.All(h.Storage.Objects.Keys, key => Assert.EndsWith(".webp", key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replacing_a_logo_removes_the_one_it_replaced()
    {
        var (h, _) = await BuildAsync();
        using var _h = h;

        await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());
        var first = h.Storage.Objects.Keys.Single();

        await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());

        Assert.Single(h.Storage.Objects);
        Assert.Contains(first, h.Storage.Deleted);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused_on_its_bytes()
    {
        var (h, _) = await BuildAsync();
        using var _h = h;

        var bytes = "definitely not an image, whatever the header claims"u8.ToArray();
        var upload = new Application.Listings.ListingMediaUpload(
            new MemoryStream(bytes), "logo.jpg", "image/jpeg", bytes.Length);

        var result = await h.Media.ReplaceAsync(StoreImageKind.Logo, upload);

        Assert.False(result.Succeeded);
        Assert.True(result.FieldErrors!.ContainsKey("file"));
        Assert.Empty(h.Storage.Objects);
    }

    [Fact]
    public async Task An_oversized_file_is_refused()
    {
        var (h, _) = await BuildAsync();
        using var _h = h;

        var result = await h.Media.ReplaceAsync(
            StoreImageKind.Logo, StoreTestHarness.Upload(length: 5 * 1024 * 1024 + 1));

        Assert.False(result.Succeeded);
        Assert.Contains("5 MB", result.FieldErrors!["file"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Heic_is_reported_distinctly_from_a_corrupt_file()
    {
        var (h, _) = await BuildAsync();
        using var _h = h;

        h.ImageProcessor.NextFailure = ImageProcessingFailure.UnsupportedFormat;

        var result = await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());

        Assert.Contains("HEIC", result.FieldErrors!["file"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declared_HEIC_logo_is_refused_at_the_door_not_after_processing()
    {
        var (h, _) = await BuildAsync();
        using var _h = h;

        var result = await h.Media.ReplaceAsync(
            StoreImageKind.Logo, StoreTestHarness.Upload(contentType: "image/heic"));

        Assert.False(result.Succeeded);
        Assert.Contains("JPEG", result.FieldErrors!["file"][0], StringComparison.Ordinal);
        Assert.Equal(0, h.ImageProcessor.Calls);
    }

    [Fact]
    public async Task Removing_a_logo_deletes_it_from_storage()
    {
        var (h, _) = await BuildAsync();
        using var _h = h;

        await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());
        var result = await h.Media.RemoveAsync(StoreImageKind.Logo);

        Assert.Null(result.Value!.LogoUrl);
        Assert.Empty(h.Storage.Objects);
    }

    [Fact]
    public async Task Somebody_without_a_store_cannot_upload_one()
    {
        var (h, _) = await BuildAsync();
        using var _h = h;

        h.ActAsOtherUser();
        var result = await h.Media.ReplaceAsync(StoreImageKind.Logo, StoreTestHarness.Upload());

        Assert.Equal(ResultError.NotFound, result.Error);
    }
}
