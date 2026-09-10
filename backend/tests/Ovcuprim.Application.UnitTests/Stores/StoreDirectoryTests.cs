using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Stores;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Stores;

/// <summary>
/// The public directory, and in particular what "this category" means here.
/// </summary>
/// <remarks>
/// Listings can only be filed in leaf categories, so a directory that matched a category exactly
/// would return nothing for every parent a visitor can actually pick. The filter therefore means
/// the category and everything beneath it — the same rule the catalogue applies, resolved through
/// the same <see cref="Application.Categories.ICategoryService"/> helper.
/// </remarks>
public class StoreDirectoryTests
{
    private static async Task<StoreTestHarness> BuildAsync()
    {
        var harness = new StoreTestHarness();
        await harness.SeedAsync();

        return harness;
    }

    /// <summary>A second storefront, owned by someone else, so the two can be told apart.</summary>
    private static async Task<Store> SecondStoreAsync(StoreTestHarness h, string name, string slug)
    {
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            PhoneNumber = "+994503334455",
            IsPhoneVerified = true,
            FullName = "İkinci Satıcı",
            Role = UserRole.User,
            Status = UserStatus.Active,
            CreatedAt = h.Clock.UtcNow
        };

        var store = new Store
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = owner.Id,
            Name = name,
            Slug = slug,
            Status = StoreStatus.Active,
            CreatedAt = h.Clock.UtcNow
        };

        h.Db.Users.Add(owner);
        h.Db.Stores.Add(store);
        await h.Db.SaveChangesAsync();

        return store;
    }

    /// <summary>Puts a live listing in a category, filed under a store, without going through publish.</summary>
    private static async Task LiveListingAsync(StoreTestHarness h, Guid storeId, Guid ownerId, int categoryId)
    {
        var region = await h.Db.Regions.FirstAsync(r => r.Slug == h.RegionSlug);

        h.Db.Listings.Add(new Listing
        {
            Id = Guid.CreateVersion7(),
            Slug = $"elan-{Guid.NewGuid():N}",
            UserId = ownerId,
            StoreId = storeId,
            CategoryId = categoryId,
            RegionId = region.Id,
            Title = "Yoxlama elanı",
            Description = "Yoxlama.",
            Currency = "AZN",
            Condition = ListingCondition.Used,
            SellerType = SellerType.Store,
            ContactPhone = "+994501112233",
            ShowPhone = true,
            Status = ListingStatus.Active,
            PublishedAt = h.Clock.UtcNow,
            BumpedAt = h.Clock.UtcNow,
            CreatedAt = h.Clock.UtcNow
        });

        await h.Db.SaveChangesAsync();
    }

    private static Task<Result<PagedResult<StoreCardDto>>> DirectoryAsync(
        StoreTestHarness h, string? category = null) =>
        h.Stores.GetDirectoryAsync(category, null, new PageRequest());

    [Fact]
    public async Task Without_a_category_every_active_storefront_is_listed()
    {
        using var h = await BuildAsync();
        await h.ActiveStoreAsync();
        await SecondStoreAsync(h, "İkinci Mağaza", "ikinci-magaza");

        var directory = await DirectoryAsync(h);

        Assert.Equal(2, directory.Value!.Items.Count);
    }

    [Fact]
    public async Task A_root_category_finds_a_store_selling_in_one_of_its_leaves()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();
        await h.StoreListingAsync();

        // The listing sits in "cadirlar"; the visitor picks its parent, "kamp". This is the case
        // that returned nothing when the filter matched a category exactly.
        var byRoot = await DirectoryAsync(h, h.RootSlug);

        Assert.Equal(store.Slug, Assert.Single(byRoot.Value!.Items).Slug);
    }

    [Fact]
    public async Task A_leaf_category_still_finds_it()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();
        await h.StoreListingAsync();

        var byLeaf = await DirectoryAsync(h, h.LeafSlug);

        Assert.Equal(store.Slug, Assert.Single(byLeaf.Value!.Items).Slug);
    }

    [Fact]
    public async Task A_sibling_leaf_the_store_does_not_sell_in_finds_nothing()
    {
        using var h = await BuildAsync();
        await h.ActiveStoreAsync();
        await h.StoreListingAsync();

        var bySibling = await DirectoryAsync(h, h.RestrictedSlug);

        Assert.Empty(bySibling.Value!.Items);
    }

    [Fact]
    public async Task Each_leaf_returns_only_the_store_selling_there_and_the_root_returns_both()
    {
        using var h = await BuildAsync();

        var first = await h.ActiveStoreAsync();
        await h.StoreListingAsync();

        var second = await SecondStoreAsync(h, "İkinci Mağaza", "ikinci-magaza");
        await LiveListingAsync(h, second.Id, second.OwnerUserId, h.RestrictedCategoryId);

        var byLeaf = await DirectoryAsync(h, h.LeafSlug);
        var bySibling = await DirectoryAsync(h, h.RestrictedSlug);
        var byRoot = await DirectoryAsync(h, h.RootSlug);

        Assert.Equal(first.Slug, Assert.Single(byLeaf.Value!.Items).Slug);
        Assert.Equal(second.Slug, Assert.Single(bySibling.Value!.Items).Slug);

        // The parent is the union of what hangs beneath it.
        Assert.Equal(2, byRoot.Value!.Items.Count);
    }

    [Fact]
    public async Task Only_live_listings_put_a_store_in_a_category()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();
        var id = await h.StoreListingAsync();

        Assert.Single((await DirectoryAsync(h, h.RootSlug)).Value!.Items);

        h.ActAsSeller();
        await h.Listings.DeleteAsync(id);

        // The storefront is still open; it just no longer sells anything under that category.
        Assert.Empty((await DirectoryAsync(h, h.RootSlug)).Value!.Items);
        Assert.Single((await DirectoryAsync(h)).Value!.Items);
        Assert.Equal(store.Slug, (await DirectoryAsync(h)).Value!.Items[0].Slug);
    }

    [Fact]
    public async Task A_suspended_store_is_absent_whichever_way_the_directory_is_filtered()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();
        await h.StoreListingAsync();

        h.ActAsAdmin();
        await h.Admin.SuspendAsync(store.Id, "Yoxlama.");

        Assert.Empty((await DirectoryAsync(h)).Value!.Items);
        Assert.Empty((await DirectoryAsync(h, h.RootSlug)).Value!.Items);
        Assert.Empty((await DirectoryAsync(h, h.LeafSlug)).Value!.Items);
    }

    [Fact]
    public async Task An_unknown_category_is_a_field_error_rather_than_an_empty_page()
    {
        using var h = await BuildAsync();
        await h.ActiveStoreAsync();

        var directory = await DirectoryAsync(h, "belə-kateqoriya-yoxdur");

        Assert.False(directory.Succeeded);
        Assert.True(directory.FieldErrors!.ContainsKey("category"));
    }
}

/// <summary>
/// The shared subtree helper itself. Both the catalogue and the store directory resolve a category
/// through this, so it is the one place the meaning of "this category" is defined.
/// </summary>
public class CategorySubtreeTests
{
    [Fact]
    public async Task A_root_resolves_to_itself_and_every_descendant()
    {
        using var h = new Listings.ListingTestHarness();
        await h.SeedAsync();

        var ids = await h.Categories.GetSubtreeIdsAsync("kamp");

        Assert.NotNull(ids);
        Assert.Equal(3, ids!.Length);
        Assert.Contains(h.LeafCategoryId, ids);
        Assert.Contains(h.RestrictedCategoryId, ids);
    }

    [Fact]
    public async Task A_leaf_resolves_to_itself_alone()
    {
        using var h = new Listings.ListingTestHarness();
        await h.SeedAsync();

        var ids = await h.Categories.GetSubtreeIdsAsync(h.LeafSlug);

        Assert.NotNull(ids);
        Assert.Equal([h.LeafCategoryId], ids!);
    }

    [Fact]
    public async Task An_unknown_slug_resolves_to_nothing_at_all()
    {
        using var h = new Listings.ListingTestHarness();
        await h.SeedAsync();

        // Null rather than empty: "no such category" and "a category with no listings" are
        // different answers, and the callers report them differently.
        Assert.Null(await h.Categories.GetSubtreeIdsAsync("yoxdur"));
    }
}
