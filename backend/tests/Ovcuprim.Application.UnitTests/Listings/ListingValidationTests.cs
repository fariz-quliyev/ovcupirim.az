using System.Text.Json;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>
/// Server-side validation is the enforcement, not a convenience: these go through the service, not
/// through the validator in isolation, so a request that skips the UI is still rejected.
/// </summary>
public class ListingValidationTests
{
    private static async Task<ListingTestHarness> BuildAsync()
    {
        var harness = new ListingTestHarness();
        await harness.SeedAsync();
        return harness;
    }

    [Fact]
    public async Task A_missing_required_attribute_is_rejected()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(
            h.NewListing(attributes: ListingTestHarness.Attributes()));

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("attributes.capacity_person"));
    }

    [Fact]
    public async Task An_unknown_attribute_key_is_rejected_rather_than_stored()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing(
            attributes: ListingTestHarness.Attributes(
                ("capacity_person", "\"2\""),
                ("smuggled_key", "\"x\""))));

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("attributes.smuggled_key"));
    }

    [Fact]
    public async Task An_option_outside_the_schema_is_rejected()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing(
            attributes: ListingTestHarness.Attributes(("capacity_person", "\"9\""))));

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("attributes.capacity_person"));
    }

    [Fact]
    public async Task A_number_outside_its_bounds_is_rejected()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing(
            attributes: ListingTestHarness.Attributes(
                ("capacity_person", "\"2\""),
                ("weight", "500"))));

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("attributes.weight"));
    }

    [Fact]
    public async Task Numbers_are_stored_as_json_numbers_rounded_to_the_schema_precision()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing(
            attributes: ListingTestHarness.Attributes(
                ("capacity_person", "\"2\""),
                ("weight", "2.345"))));

        Assert.True(created.Succeeded);

        var stored = (await h.ReloadAsync(created.Value!.Id)).Attributes;

        Assert.Equal(JsonValueKind.Number, stored["weight"].ValueKind);
        Assert.Equal(2.35m, stored["weight"].GetDecimal());
        Assert.Equal(JsonValueKind.String, stored["capacity_person"].ValueKind);
    }

    [Fact]
    public async Task A_non_leaf_category_cannot_carry_a_listing()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing(categorySlug: "kamp"));

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("categorySlug"));
    }

    [Fact]
    public async Task An_unknown_category_is_rejected()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing(categorySlug: "yoxdur"));

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("categorySlug"));
    }

    [Fact]
    public async Task A_non_selectable_region_is_rejected_at_creation()
    {
        using var h = await BuildAsync();

        var request = h.NewListing() with { RegionSlug = "nesimi" };
        var created = await h.Listings.CreateDraftAsync(request);

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("regionSlug"));
    }

    [Fact]
    public async Task A_region_deactivated_after_drafting_is_caught_at_publish()
    {
        using var h = await BuildAsync();
        var id = await h.DraftWithImageAsync();

        var region = h.Db.Regions.Single(r => r.Slug == h.RegionSlug);
        region.IsActive = false;
        await h.Db.SaveChangesAsync();

        var published = await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        Assert.False(published.Succeeded);
        Assert.True(published.FieldErrors!.ContainsKey("regionSlug"));
    }

    [Fact]
    public async Task An_invalid_phone_number_is_rejected()
    {
        using var h = await BuildAsync();

        var request = h.NewListing() with { ContactPhone = "12345" };
        var created = await h.Listings.CreateDraftAsync(request);

        Assert.False(created.Succeeded);
        Assert.True(created.FieldErrors!.ContainsKey("contactPhone"));
    }

    [Fact]
    public async Task The_phone_number_is_normalised_to_e164()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing());

        Assert.Equal("+994501112233", created.Value!.ContactPhone);
    }

    [Fact]
    public async Task A_restricted_category_requires_age_confirmation()
    {
        using var h = await BuildAsync();
        var id = await h.DraftWithImageAsync(h.RestrictedSlug);

        var without = await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        Assert.False(without.Succeeded);
        Assert.True(without.FieldErrors!.ContainsKey("ageConfirmed"));

        var with = await h.Publishing.PublishAsync(id, new PublishListingRequest(true));

        Assert.True(with.Succeeded);
        Assert.True(with.Value!.AgeConfirmed);
    }

    [Fact]
    public async Task An_unclassified_category_is_gated_the_same_way()
    {
        using var h = await BuildAsync();

        var category = h.Db.Categories.Single(c => c.Slug == h.RestrictedSlug);
        category.RestrictionStatus = RestrictionStatus.Unclassified;
        await h.Db.SaveChangesAsync();

        var id = await h.DraftWithImageAsync(h.RestrictedSlug);
        var published = await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        Assert.False(published.Succeeded);
        Assert.True(published.FieldErrors!.ContainsKey("ageConfirmed"));
    }

    [Fact]
    public async Task An_unrestricted_category_needs_no_confirmation()
    {
        using var h = await BuildAsync();
        var id = await h.DraftWithImageAsync();

        var published = await h.Publishing.PublishAsync(id, new PublishListingRequest(false));

        Assert.True(published.Succeeded);
        Assert.False(published.Value!.AgeConfirmed);
    }

    [Fact]
    public async Task A_zero_price_is_kept_and_a_null_price_stays_null()
    {
        using var h = await BuildAsync();

        var free = await h.Listings.CreateDraftAsync(h.NewListing(price: 0m));
        var negotiable = await h.Listings.CreateDraftAsync(h.NewListing(price: null));

        Assert.Equal(0m, free.Value!.Price);
        Assert.Null(negotiable.Value!.Price);
    }

    [Fact]
    public async Task The_slug_is_built_from_the_azerbaijani_title()
    {
        using var h = await BuildAsync();

        var request = h.NewListing() with { Title = "Çadır və işıq dəsti" };
        var created = await h.Listings.CreateDraftAsync(request);

        Assert.Equal("cadir-ve-isiq-desti", created.Value!.Slug);
    }

    [Fact]
    public async Task Display_attributes_render_labels_and_units_for_the_page()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing(
            attributes: ListingTestHarness.Attributes(
                ("capacity_person", "\"2\""),
                ("weight", "2.5"))));

        var rows = created.Value!.DisplayAttributes;

        Assert.Equal("Tutum", rows[0].LabelAz);
        Assert.Equal("2 nəfər", rows[0].DisplayValue);
        Assert.Equal("2.5 kq", rows[1].DisplayValue);
    }

    [Fact]
    public async Task A_seller_cannot_set_status_or_ownership_through_a_request()
    {
        using var h = await BuildAsync();

        var created = await h.Listings.CreateDraftAsync(h.NewListing());
        var listing = await h.ReloadAsync(created.Value!.Id);

        // The request DTO carries none of these; they are the service's alone.
        Assert.Equal(ListingStatus.Draft, listing.Status);
        Assert.Equal(h.SellerId, listing.UserId);
        Assert.Equal(0, listing.ViewCount);
        Assert.Null(listing.PublishedAt);
        Assert.Equal(SellerType.Individual, listing.SellerType);
        Assert.Null(listing.StoreId);
    }

    [Fact]
    public async Task Another_user_cannot_read_or_change_a_listing()
    {
        using var h = await BuildAsync();
        var created = await h.Listings.CreateDraftAsync(h.NewListing());
        var id = created.Value!.Id;

        h.ActAsOtherUser();

        var read = await h.Listings.GetAsync(id);
        var update = await h.Listings.UpdateAsync(id, ListingLifecycleTests.UpdateFrom(h));
        var deleted = await h.Listings.DeleteAsync(id);

        Assert.Equal(ResultError.NotFound, read.Error);
        Assert.Equal(ResultError.NotFound, update.Error);
        Assert.Equal(ResultError.NotFound, deleted.Error);
    }

    [Fact]
    public async Task A_moderator_may_read_any_listing()
    {
        using var h = await BuildAsync();
        var created = await h.Listings.CreateDraftAsync(h.NewListing());

        h.ActAsModerator();
        var read = await h.Listings.GetAsync(created.Value!.Id);

        Assert.True(read.Succeeded);
    }
}
