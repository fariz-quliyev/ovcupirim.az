using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Stores;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Stores;

/// <summary>
/// Where a storefront meets the listing domain: who may file a listing under a store, what
/// SellerType is allowed to say, and what the public sees once a storefront closes.
/// </summary>
public class StoreListingBoundaryTests
{
    private static async Task<StoreTestHarness> BuildAsync()
    {
        var harness = new StoreTestHarness();
        await harness.SeedAsync();
        harness.ActAsSeller();

        return harness;
    }

    [Fact]
    public async Task A_listing_without_a_store_is_an_individual_sale()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing());
        var listing = await h.ReloadAsync(created.Value!.Id);

        Assert.Null(listing.StoreId);
        Assert.Equal(SellerType.Individual, listing.SellerType);
        Assert.Equal(nameof(SellerType.Individual), created.Value.SellerType);
    }

    [Fact]
    public async Task A_listing_filed_under_an_active_store_becomes_a_store_sale()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing(useStore: true));
        var listing = await h.ReloadAsync(created.Value!.Id);

        Assert.Equal(store.Id, listing.StoreId);
        // Derived from the outcome, never taken from the request.
        Assert.Equal(SellerType.Store, listing.SellerType);
    }

    [Fact]
    public async Task A_seller_with_no_store_cannot_file_one_under_a_store()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing(useStore: true));

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("useStore"));
    }

    [Fact]
    public async Task A_pending_store_cannot_take_listings_yet()
    {
        using var h = await BuildAsync();
        await h.Stores.ApplyAsync(StoreTestHarness.Application());

        var created = await h.Listings.CreateDraftAsync(h.NewListing(useStore: true));

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("useStore"));
    }

    [Fact]
    public async Task A_suspended_store_cannot_take_new_listings()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();

        h.ActAsAdmin();
        await h.Admin.SuspendAsync(store.Id, "Yoxlama.");

        h.ActAsSeller();
        var created = await h.Listings.CreateDraftAsync(h.NewListing(useStore: true));

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("useStore"));
    }

    [Fact]
    public async Task A_seller_can_never_reach_another_sellers_store()
    {
        using var h = await BuildAsync();
        await h.ActiveStoreAsync();

        // Nothing in the request names a store; it is resolved from the caller alone, so somebody
        // else asking for "my store" simply has none.
        h.ActAsOtherUser();
        var created = await h.Listings.CreateDraftAsync(h.NewListing(useStore: true));

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("useStore"));
    }

    [Fact]
    public async Task The_public_page_of_a_store_listing_carries_the_store_block()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();
        var id = await h.StoreListingAsync();

        var shortId = (await h.ReloadAsync(id)).ShortId;
        var page = await h.Listings.GetPublicAsync(shortId, ageConfirmed: false);

        Assert.NotNull(page.Value!.Store);
        Assert.Equal(store.Slug, page.Value.Store!.Slug);
        Assert.Equal("Ovçu Dünyası", page.Value.Store.Name);
    }

    [Fact]
    public async Task An_individual_listing_has_no_store_block()
    {
        using var h = await BuildAsync();

        var id = await ListingHelpers.LiveIndividualListingAsync(h);
        var shortId = (await h.ReloadAsync(id)).ShortId;

        var page = await h.Listings.GetPublicAsync(shortId, ageConfirmed: false);

        Assert.Null(page.Value!.Store);
    }

    [Fact]
    public async Task Suspending_a_store_leaves_its_listings_live_and_readable()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();
        var id = await h.StoreListingAsync();
        var shortId = (await h.ReloadAsync(id)).ShortId;

        h.ActAsAdmin();
        await h.Admin.SuspendAsync(store.Id, "Yoxlama.");

        var listing = await h.ReloadAsync(id);
        var page = await h.Listings.GetPublicAsync(shortId, ageConfirmed: false);

        // The shopfront closed; the goods did not.
        Assert.Equal(ListingStatus.Active, listing.Status);
        Assert.True(page.Succeeded);
    }

    [Fact]
    public async Task A_suspended_stores_listing_offers_no_way_through_to_the_store()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();
        var id = await h.StoreListingAsync();
        var shortId = (await h.ReloadAsync(id)).ShortId;

        h.ActAsAdmin();
        await h.Admin.SuspendAsync(store.Id, "Yoxlama.");

        var page = await h.Listings.GetPublicAsync(shortId, ageConfirmed: false);

        // No block, so no name, no link and no claim that the storefront is open.
        Assert.Null(page.Value!.Store);
        Assert.Equal(ResultError.NotFound, (await h.Stores.GetPublicAsync(store.Slug)).Error);
    }

    [Fact]
    public async Task A_suspended_stores_listing_keeps_its_seller_type()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();
        var id = await h.StoreListingAsync();

        h.ActAsAdmin();
        await h.Admin.SuspendAsync(store.Id, "Yoxlama.");

        // SellerType is a fact about how the listing was placed, not a claim about the storefront's
        // current state, so it is left alone. What disappears is the navigation.
        Assert.Equal(SellerType.Store, (await h.ReloadAsync(id)).SellerType);
    }

    [Fact]
    public async Task Store_listings_are_counted_only_while_they_are_live()
    {
        using var h = await BuildAsync();
        var store = await h.ActiveStoreAsync();
        var id = await h.StoreListingAsync();

        Assert.Equal(1, await h.Db.Listings.CountAsync(
            l => l.StoreId == store.Id && l.Status == ListingStatus.Active));

        h.ActAsSeller();
        await h.Listings.DeleteAsync(id);

        Assert.Equal(0, await h.Db.Listings.CountAsync(
            l => l.StoreId == store.Id && l.Status == ListingStatus.Active));
    }
}

internal static class ListingHelpers
{
    /// <summary>A live listing with no storefront behind it.</summary>
    public static async Task<Guid> LiveIndividualListingAsync(StoreTestHarness h)
    {
        h.ActAsSeller();

        var created = await h.Listings.CreateDraftAsync(h.NewListing());
        var id = created.Value!.Id;

        await h.ListingMedia.AddAsync(id, StoreTestHarness.Upload());
        await h.Publishing.PublishAsync(id, new Application.Listings.PublishListingRequest(false));

        h.ActAsModerator();
        await h.Moderation.ApproveAsync(id);
        h.ActAsSeller();

        return id;
    }
}
