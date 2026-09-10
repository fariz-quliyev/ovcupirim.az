namespace Ovcuprim.Application.Listings.Search;

/// <summary>
/// A listing as it appears in a result grid. Deliberately narrow — no description, no attribute
/// bag, no phone number — because a page of sixty of these travels on every catalogue request.
/// </summary>
public sealed record ListingCardDto(
    long ShortId,
    string Slug,
    string Path,
    string Title,
    decimal? Price,
    string Currency,
    string RegionNameAz,
    string CategorySlug,
    string CategoryNameAz,
    string? ImageUrl,
    bool HasDelivery,
    string Condition,
    string SellerType,
    bool RequiresAgeConfirmation,
    bool IsFavorited,
    DateTimeOffset PublishedAt);

public sealed record ListingSearchResultDto(
    IReadOnlyList<ListingCardDto> Items,
    int Page,
    int PageSize,
    int Total,
    /// <summary>False when the count stopped at its cap; the UI shows "10 000+" rather than a number.</summary>
    bool TotalIsExact,
    int TotalPages,
    string Sort);

public sealed record FacetItemDto(string Slug, string NameAz, int Count);

public sealed record ListingFacetsDto(
    IReadOnlyList<FacetItemDto> Categories,
    IReadOnlyList<FacetItemDto> Regions);
