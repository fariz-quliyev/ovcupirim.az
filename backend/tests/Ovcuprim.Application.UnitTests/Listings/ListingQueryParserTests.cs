using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings.Search;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Listings;

/// <summary>
/// The parser is the whitelist that makes the raw SQL safe. Everything here is about what survives
/// it and what is dropped before a statement is ever composed.
/// </summary>
public class ListingQueryParserTests
{
    private static async Task<(ListingTestHarness Harness, IListingQueryParser Parser)> BuildAsync()
    {
        var harness = new ListingTestHarness();
        await harness.SeedAsync();

        return (harness, new ListingQueryParser(harness.Db, harness.Categories));
    }

    private static ListingSearchRequest Request(
        string? category = null,
        Dictionary<string, string>? attributes = null) =>
        new()
        {
            Category = category,
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };

    [Fact]
    public async Task An_empty_request_asks_for_everything_newest_first()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(new ListingSearchRequest())).Value!;

        Assert.Empty(query.CategoryIds);
        Assert.Null(query.RegionId);
        Assert.Empty(query.Attributes);
        Assert.Equal(ListingSort.Newest, query.Sort);
        Assert.False(query.HasText);
    }

    [Fact]
    public async Task A_category_expands_to_itself_and_its_descendants()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var parent = (await parser.ParseAsync(Request(category: "kamp"))).Value!;
        var leaf = (await parser.ParseAsync(Request(category: h.LeafSlug))).Value!;

        // Browsing a parent has to show what is filed under its children.
        Assert.Equal(3, parent.CategoryIds.Count);
        Assert.Contains(h.LeafCategoryId, parent.CategoryIds);
        Assert.Single(leaf.CategoryIds);
    }

    [Fact]
    public async Task An_unknown_category_is_a_field_error()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var result = await parser.ParseAsync(Request(category: "yoxdur"));

        Assert.False(result.Succeeded);
        Assert.True(result.FieldErrors!.ContainsKey("category"));
    }

    [Fact]
    public async Task A_non_selectable_region_is_refused_the_same_way_a_seller_would_be()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var result = await parser.ParseAsync(new ListingSearchRequest { Region = "nesimi" });

        Assert.False(result.Succeeded);
        Assert.True(result.FieldErrors!.ContainsKey("region"));
    }

    [Fact]
    public async Task Search_text_is_folded_the_same_way_the_search_key_was_built()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(new ListingSearchRequest { Q = "Çadır" })).Value!;

        Assert.True(query.HasText);
        Assert.Equal("cadir", query.Text);
        Assert.Equal("Çadır", query.RawText);
    }

    [Fact]
    public async Task An_unknown_attribute_key_never_reaches_the_query()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(Request(
            h.LeafSlug,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["definitely_not_a_key"] = "x",
                ["'; DROP TABLE \"Listings\"; --"] = "1"
            }))).Value!;

        Assert.Empty(query.Attributes);
    }

    [Fact]
    public async Task Attribute_filters_are_dropped_entirely_outside_a_category()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        // Nothing to validate them against, so they are ignored rather than trusted.
        var query = (await parser.ParseAsync(Request(
            category: null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["weight_min"] = "1" }))).Value!;

        Assert.Empty(query.Attributes);
    }

    [Fact]
    public async Task A_numeric_range_survives_with_both_bounds()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(Request(
            h.LeafSlug,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["weight_min"] = "1.5",
                ["weight_max"] = "4"
            }))).Value!;

        var range = Assert.IsType<AttributeFilter.Range>(Assert.Single(query.Attributes));

        Assert.Equal("weight", range.Key);
        Assert.Equal(1.5m, range.Min);
        Assert.Equal(4m, range.Max);
    }

    [Fact]
    public async Task A_comma_decimal_is_accepted_because_the_form_accepts_one_on_input()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(Request(
            h.LeafSlug,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["weight_min"] = "2,5" }))).Value!;

        var range = Assert.IsType<AttributeFilter.Range>(Assert.Single(query.Attributes));

        Assert.Equal(2.5m, range.Min);
    }

    [Fact]
    public async Task A_non_numeric_range_bound_is_discarded_rather_than_passed_through()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(Request(
            h.LeafSlug,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["weight_min"] = "1); DROP TABLE x --" }))).Value!;

        Assert.Empty(query.Attributes);
    }

    [Fact]
    public async Task A_select_filter_becomes_canonical_containment_json()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(Request(
            h.LeafSlug,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["capacity_person"] = "2" }))).Value!;

        var containment = Assert.IsType<AttributeFilter.Containment>(Assert.Single(query.Attributes));

        Assert.Equal("""{"capacity_person":"2"}""", Assert.Single(containment.JsonValues));
    }

    [Fact]
    public async Task Several_option_values_become_an_any_of_group()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(Request(
            h.LeafSlug,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["capacity_person"] = "2,4" }))).Value!;

        var containment = Assert.IsType<AttributeFilter.Containment>(Assert.Single(query.Attributes));

        Assert.Equal(2, containment.JsonValues.Count);
    }

    [Fact]
    public async Task An_option_value_outside_the_schema_is_discarded()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(Request(
            h.LeafSlug,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["capacity_person"] = "2,999" }))).Value!;

        var containment = Assert.IsType<AttributeFilter.Containment>(Assert.Single(query.Attributes));

        // The known value survives; the invented one is gone.
        Assert.Single(containment.JsonValues);
        Assert.Contains("\"2\"", containment.JsonValues[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Relevance_without_a_search_term_falls_back_to_newest()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var withoutText = (await parser.ParseAsync(new ListingSearchRequest { Sort = "relevance" })).Value!;
        var withText = (await parser.ParseAsync(new ListingSearchRequest { Sort = "relevance", Q = "çadır" })).Value!;

        Assert.Equal(ListingSort.Newest, withoutText.Sort);
        Assert.Equal(ListingSort.Relevance, withText.Sort);
    }

    [Fact]
    public async Task An_unknown_sort_falls_back_instead_of_failing()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(new ListingSearchRequest { Sort = "'; DROP TABLE x --" })).Value!;

        Assert.Equal(ListingSort.Newest, query.Sort);
    }

    [Fact]
    public async Task Paging_is_clamped_at_both_ends()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var tooSmall = (await parser.ParseAsync(new ListingSearchRequest { Page = -5, PageSize = 0 })).Value!;
        var tooLarge = (await parser.ParseAsync(new ListingSearchRequest { Page = 9_999, PageSize = 5_000 })).Value!;

        Assert.Equal(1, tooSmall.Page);
        Assert.Equal(PageRequest.DefaultPageSize, tooSmall.PageSize);
        Assert.Equal(ListingQueryParser.MaxPage, tooLarge.Page);
        Assert.Equal(PageRequest.MaxPageSize, tooLarge.PageSize);
    }

    [Fact]
    public async Task A_reversed_price_range_is_a_field_error()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var result = await parser.ParseAsync(new ListingSearchRequest { PriceMin = 500, PriceMax = 100 });

        Assert.False(result.Succeeded);
        Assert.True(result.FieldErrors!.ContainsKey("priceMin"));
    }

    [Fact]
    public async Task A_negative_price_is_a_field_error()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var result = await parser.ParseAsync(new ListingSearchRequest { PriceMin = -1 });

        Assert.False(result.Succeeded);
        Assert.True(result.FieldErrors!.ContainsKey("priceMin"));
    }

    [Fact]
    public async Task Condition_delivery_and_seller_type_are_parsed_case_insensitively()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(new ListingSearchRequest
        {
            Condition = "new",
            Delivery = true,
            SellerType = "INDIVIDUAL"
        })).Value!;

        Assert.Equal(ListingCondition.New, query.Condition);
        Assert.True(query.HasDelivery);
        Assert.Equal(SellerType.Individual, query.SellerType);
    }

    [Fact]
    public async Task A_nonsense_enum_is_ignored_rather_than_rejected()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var query = (await parser.ParseAsync(new ListingSearchRequest { Condition = "banana" })).Value!;

        Assert.Null(query.Condition);
    }

    [Fact]
    public async Task A_store_pin_survives_the_parser_untouched()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var storeId = Guid.CreateVersion7();

        var query = (await parser.ParseAsync(new ListingSearchRequest { StoreId = storeId })).Value!;

        // The storefront grid is the ordinary catalogue with one dimension pinned by the server.
        Assert.Equal(storeId, query.StoreId);
    }

    [Fact]
    public async Task A_store_pin_composes_with_every_other_dimension()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        var storeId = Guid.CreateVersion7();

        var query = (await parser.ParseAsync(new ListingSearchRequest
        {
            StoreId = storeId,
            Category = h.LeafSlug,
            Region = h.RegionSlug,
            PriceMin = 10,
            PriceMax = 500,
            Condition = "New",
            Delivery = true,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["capacity_person"] = "2" }
        })).Value!;

        // Nothing about pinning a store weakens the rest of the whitelist.
        Assert.Equal(storeId, query.StoreId);
        Assert.Single(query.CategoryIds);
        Assert.NotNull(query.RegionId);
        Assert.Equal(10, query.PriceMin);
        Assert.Equal(500, query.PriceMax);
        Assert.Equal(ListingCondition.New, query.Condition);
        Assert.True(query.HasDelivery);
        Assert.Single(query.Attributes);
    }

    [Fact]
    public async Task A_store_pin_is_absent_unless_the_server_sets_one()
    {
        var (h, parser) = await BuildAsync();
        using var _ = h;

        // There is no query-string spelling of "store": the only way one is set is the storefront
        // route resolving it from the slug, so an ordinary catalogue request can never carry one.
        var query = (await parser.ParseAsync(new ListingSearchRequest
        {
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["store"] = Guid.CreateVersion7().ToString(),
                ["storeId"] = Guid.CreateVersion7().ToString()
            }
        })).Value!;

        Assert.Null(query.StoreId);
        Assert.Empty(query.Attributes);
    }
}

