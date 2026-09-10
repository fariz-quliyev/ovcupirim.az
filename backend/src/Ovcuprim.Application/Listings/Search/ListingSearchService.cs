using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Regions;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings.Search;

public interface IListingSearchService
{
    Task<Result<ListingSearchResultDto>> SearchAsync(
        ListingSearchRequest request, CancellationToken cancellationToken = default);

    Task<Result<ListingFacetsDto>> FacetsAsync(
        ListingSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>"Bənzər elanlar": same category, newest first, never the listing itself.</summary>
    Task<Result<IReadOnlyList<ListingCardDto>>> SimilarAsync(
        long shortId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cards for a known set of ids, in the order given. Shared so favourites and similar listings
    /// render through exactly the same query and mapping as the catalogue.
    /// </summary>
    Task<IReadOnlyList<ListingCardDto>> CardsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);
}

public sealed class ListingSearchService(
    IAppDbContext db,
    IListingSearchStore store,
    IListingQueryParser parser,
    ICategoryService categories,
    IRegionService regions,
    ICurrentUser currentUser,
    IFileStorage storage) : IListingSearchService
{
    private const int SimilarCount = 8;

    public async Task<Result<ListingSearchResultDto>> SearchAsync(
        ListingSearchRequest request, CancellationToken cancellationToken = default)
    {
        var parsed = await parser.ParseAsync(request, cancellationToken);

        if (!parsed.Succeeded)
        {
            return Result<ListingSearchResultDto>.From(parsed);
        }

        var query = parsed.Value!;
        var page = await store.SearchAsync(query, cancellationToken);
        var cards = await HydrateAsync(page.Ids, cancellationToken);

        var totalPages = query.PageSize <= 0 ? 0 : (int)Math.Ceiling(page.Total / (double)query.PageSize);

        return Result<ListingSearchResultDto>.Success(new ListingSearchResultDto(
            cards,
            query.Page,
            query.PageSize,
            page.Total,
            page.TotalIsExact,
            totalPages,
            SortName(query.Sort)));
    }

    public async Task<Result<ListingFacetsDto>> FacetsAsync(
        ListingSearchRequest request, CancellationToken cancellationToken = default)
    {
        // Parsed through the same parser as the result page, so the two can never disagree about
        // what the current filter set means.
        var parsed = await parser.ParseAsync(request, cancellationToken);

        if (!parsed.Succeeded)
        {
            return Result<ListingFacetsDto>.From(parsed);
        }

        var facets = await store.FacetsAsync(parsed.Value!, cancellationToken);

        var categoryTree = await categories.GetTreeAsync(cancellationToken);
        var categoryLookup = new Dictionary<int, (string Slug, string Name)>();
        Flatten(categoryTree, categoryLookup);

        var regionLookup = (await regions.GetAllAsync(cancellationToken))
            .ToDictionary(r => r.Id, r => (r.Slug, Name: r.NameAz));

        return Result<ListingFacetsDto>.Success(new ListingFacetsDto(
            Project(facets.Categories, categoryLookup),
            Project(facets.Regions, regionLookup)));
    }

    public async Task<Result<IReadOnlyList<ListingCardDto>>> SimilarAsync(
        long shortId, CancellationToken cancellationToken = default)
    {
        var listing = await db.Listings.AsNoTracking()
            .Where(l => l.ShortId == shortId && l.Status == ListingStatus.Active)
            .Select(l => new { l.Id, l.CategoryId })
            .FirstOrDefaultAsync(cancellationToken);

        if (listing is null)
        {
            return Result<IReadOnlyList<ListingCardDto>>.NotFound(ListingService.NotFoundMessage);
        }

        var ids = await db.Listings.AsNoTracking()
            .Where(l => l.Status == ListingStatus.Active
                        && l.CategoryId == listing.CategoryId
                        && l.Id != listing.Id)
            .OrderByDescending(l => l.BumpedAt)
            .Select(l => l.Id)
            .Take(SimilarCount)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ListingCardDto>>.Success(await HydrateAsync(ids, cancellationToken));
    }

    public Task<IReadOnlyList<ListingCardDto>> CardsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
        HydrateAsync(ids, cancellationToken);

    /// <summary>
    /// Loads the rows for one page of ids and puts them back in the order the search returned,
    /// which is the order the database sorted them in and not the order EF happens to fetch them.
    /// </summary>
    private async Task<IReadOnlyList<ListingCardDto>> HydrateAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var listings = await db.Listings.AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .Include(l => l.Category)
            .Include(l => l.Region)
            .Include(l => l.Media)
            .ToListAsync(cancellationToken);

        var favorited = await FavoritedAsync(ids, cancellationToken);
        var restricted = await RestrictedCategoriesAsync(cancellationToken);
        var byId = listings.ToDictionary(l => l.Id);

        return [.. ids.Where(byId.ContainsKey)
            .Select(id => ToCard(byId[id], favorited.Contains(id), restricted.Contains(byId[id].CategoryId)))];
    }

    /// <summary>One query for the whole page rather than one per card.</summary>
    private async Task<HashSet<Guid>> FavoritedAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return [];
        }

        var favorited = await db.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId && ids.Contains(f.ListingId))
            .Select(f => f.ListingId)
            .ToListAsync(cancellationToken);

        return [.. favorited];
    }

    /// <summary>
    /// Categories whose effective restriction is Restricted or Unclassified. Cards still appear in
    /// browse; the card carries the flag so the grid can mark it and the detail page can gate it.
    /// </summary>
    private async Task<HashSet<int>> RestrictedCategoriesAsync(CancellationToken cancellationToken)
    {
        var tree = await categories.GetTreeAsync(cancellationToken);
        var flagged = new HashSet<int>();

        void Walk(IReadOnlyList<CategoryNodeDto> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.RequiresAgeConfirmation)
                {
                    flagged.Add(node.Id);
                }

                Walk(node.Children);
            }
        }

        Walk(tree);

        return flagged;
    }

    private ListingCardDto ToCard(Listing listing, bool isFavorited, bool requiresAgeConfirmation) =>
        new(
            listing.ShortId,
            listing.Slug,
            ListingMapper.CanonicalPath(listing),
            listing.Title,
            listing.Price,
            listing.Currency,
            listing.Region.NameAz,
            listing.Category.Slug,
            listing.Category.NameAz,
            PrimaryImageUrl(listing),
            listing.HasDelivery,
            listing.Condition.ToString(),
            listing.SellerType.ToString(),
            requiresAgeConfirmation,
            isFavorited,
            listing.PublishedAt ?? listing.CreatedAt);

    private string? PrimaryImageUrl(Listing listing)
    {
        var primary = listing.Media.FirstOrDefault(m => m.IsPrimary) ?? listing.Media.MinBy(m => m.SortOrder);

        if (primary is null)
        {
            return null;
        }

        return primary.Variants.TryGetValue("card", out var card)
            ? storage.GetPublicUrl(card)
            : storage.GetPublicUrl(primary.StorageKey);
    }

    private static void Flatten(IReadOnlyList<CategoryNodeDto> nodes, Dictionary<int, (string, string)> into)
    {
        foreach (var node in nodes)
        {
            into[node.Id] = (node.Slug, node.NameAz);
            Flatten(node.Children, into);
        }
    }

    private static IReadOnlyList<FacetItemDto> Project(
        IReadOnlyList<FacetCount> counts, Dictionary<int, (string Slug, string Name)> lookup) =>
        [.. counts
            .Where(c => lookup.ContainsKey(c.Id))
            .Select(c => new FacetItemDto(lookup[c.Id].Slug, lookup[c.Id].Name, c.Count))];

    private static string SortName(ListingSort sort) => sort switch
    {
        ListingSort.PriceAscending => "price_asc",
        ListingSort.PriceDescending => "price_desc",
        ListingSort.Relevance => "relevance",
        _ => "newest"
    };
}
