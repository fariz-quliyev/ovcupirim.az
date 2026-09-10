using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings.Search;

public enum ListingSort
{
    /// <summary>Default: most recently bumped first, which is what a classifieds visitor expects.</summary>
    Newest = 0,
    PriceAscending = 1,
    PriceDescending = 2,

    /// <summary>Full-text rank. Only meaningful when a search term is present.</summary>
    Relevance = 3
}

/// <summary>How one dynamic attribute narrows a search.</summary>
public abstract record AttributeFilter(string Key)
{
    /// <summary>
    /// Numeric bound, served by the partial expression index on
    /// <c>safe_numeric("Attributes"-&gt;&gt;'key')</c>.
    /// </summary>
    public sealed record Range(string Key, decimal? Min, decimal? Max) : AttributeFilter(Key);

    /// <summary>
    /// Containment against the GIN index. One entry per accepted value; several values on the same
    /// key mean "any of", which is how a multi-choice filter behaves.
    /// </summary>
    public sealed record Containment(string Key, IReadOnlyList<string> JsonValues) : AttributeFilter(Key);

    /// <summary>Boolean containment: exactly one JSON literal.</summary>
    public sealed record Flag(string Key, bool Value) : AttributeFilter(Key);
}

/// <summary>
/// A validated, normalised catalogue query. Nothing reaches this record that has not been checked
/// against the category schema, so every attribute key here is known to exist and every value has
/// already been coerced to its canonical type.
/// </summary>
public sealed record ListingQuery
{
    /// <summary>Accent-folded search text, normalised exactly the way SearchKey was built.</summary>
    public string? Text { get; init; }

    /// <summary>Raw text as typed, for full-text matching.</summary>
    public string? RawText { get; init; }

    /// <summary>The chosen category and, when it has descendants, all of them.</summary>
    public IReadOnlyList<int> CategoryIds { get; init; } = [];

    public int? RegionId { get; init; }

    /// <summary>Pins the results to one storefront. Set only by the store page.</summary>
    public Guid? StoreId { get; init; }

    public decimal? PriceMin { get; init; }

    public decimal? PriceMax { get; init; }

    public ListingCondition? Condition { get; init; }

    public bool? HasDelivery { get; init; }

    public SellerType? SellerType { get; init; }

    public IReadOnlyList<AttributeFilter> Attributes { get; init; } = [];

    public ListingSort Sort { get; init; } = ListingSort.Newest;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 24;

    public int Skip => (Page - 1) * PageSize;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>One page of matching listing ids, in order, plus how many matched in total.</summary>
public sealed record ListingIdPage(IReadOnlyList<Guid> Ids, int Total, bool TotalIsExact);

public sealed record FacetCount(int Id, int Count);

public sealed record ListingFacets(
    IReadOnlyList<FacetCount> Categories,
    IReadOnlyList<FacetCount> Regions);
