using Ovcuprim.Application.Listings.Search;

namespace Ovcuprim.Application.Abstractions;

/// <summary>
/// Executes catalogue queries against the database.
/// </summary>
/// <remarks>
/// Kept behind an abstraction because the implementation is unavoidably PostgreSQL-specific: the
/// dynamic attribute bag is a JSONB column reached through <c>@&gt;</c> and the
/// <c>safe_numeric(...)</c> expression indexes, neither of which EF can translate from LINQ. Every
/// statement is composed from one shared WHERE builder, so the page, the total and both facet
/// counts always describe the same set.
/// </remarks>
public interface IListingSearchStore
{
    /// <summary>Ids of one page of matching listings, in sort order, with the total match count.</summary>
    Task<ListingIdPage> SearchAsync(ListingQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Category and region counts for the same filter set. The dimension being counted is excluded
    /// from its own filter, so a facet list still shows the alternatives a visitor can switch to.
    /// </summary>
    Task<ListingFacets> FacetsAsync(ListingQuery query, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the denormalised counts the category tree and region picker display.</summary>
    Task<int> RefreshListingCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies buffered view increments in one statement.</summary>
    Task<int> ApplyViewCountsAsync(IReadOnlyDictionary<Guid, int> increments, CancellationToken cancellationToken = default);
}
