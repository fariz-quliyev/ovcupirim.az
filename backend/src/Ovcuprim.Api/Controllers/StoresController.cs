using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings.Search;
using Ovcuprim.Application.Stores;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// The public storefront surface.
/// </summary>
/// <remarks>
/// A store is public only while it is active. Anything else — awaiting approval, suspended,
/// orphaned — answers 404 with the same wording as a store that never existed, so a slug cannot be
/// used to probe the state of somebody's application.
/// </remarks>
[Authorize]
public sealed class StoresController(
    IStoreService stores,
    IStoreFollowService follows,
    IListingSearchService search) : ApiControllerBase
{
    /// <summary>
    /// The directory. Identical for every visitor, so it stays publicly cacheable.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.ListingSearch)]
    [ProducesResponseType<PagedResult<StoreCardDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StoreCardDto>>> GetDirectory(
        [FromQuery] string? category,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await stores.GetDirectoryAsync(
            category, sort, new PageRequest { Page = page, PageSize = pageSize }, cancellationToken);

        return result.Succeeded ? CachedOk(result.Value!, TimeSpan.FromMinutes(5)) : Problem(result);
    }

    /// <summary>
    /// One storefront. Carries the caller's own follow state, so this response is per-visitor and
    /// must never be held by a shared cache.
    /// </summary>
    [HttpGet("{slug}")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.ListingDetail)]
    [ProducesResponseType<StorePublicDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StorePublicDto>> Get(string slug, CancellationToken cancellationToken)
    {
        var result = await stores.GetPublicAsync(slug, cancellationToken);

        return result.Succeeded
            ? CachedOk(result.Value!, TimeSpan.FromMinutes(1), variesByUser: true)
            : Problem(result);
    }

    /// <summary>Revealed on demand, and only ever the storefront's own number.</summary>
    [HttpGet("{slug}/phone")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.PhoneReveal)]
    [ProducesResponseType<StorePhoneDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StorePhoneDto>> GetPhone(string slug, CancellationToken cancellationToken) =>
        FromResult(await stores.GetPublicPhoneAsync(slug, cancellationToken));

    /// <summary>
    /// The storefront's listings, served by the ordinary catalogue with the store pinned — the same
    /// query, filters and sorting the rest of the site uses.
    /// </summary>
    [HttpGet("{slug}/listings")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.ListingSearch)]
    [ProducesResponseType<ListingSearchResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ListingSearchResultDto>> GetListings(
        string slug, CancellationToken cancellationToken)
    {
        // Resolving it here is what enforces the rule: a storefront that is not public has no id
        // to pin, so its listings cannot be browsed through this route either.
        var storeId = await stores.GetPublicIdAsync(slug, cancellationToken);

        if (storeId is null)
        {
            return Problem(Result.NotFound(StoreService.NotFoundMessage));
        }

        var request = ReadSearchRequest(storeId.Value);
        var result = await search.SearchAsync(request, cancellationToken);

        // Cards carry the caller's own isFavorited, exactly as the catalogue does.
        return result.Succeeded
            ? CachedOk(result.Value!, TimeSpan.FromMinutes(1), variesByUser: true)
            : Problem(result);
    }

    [HttpPut("{slug}/follow")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Follow(string slug, CancellationToken cancellationToken)
    {
        var result = await follows.FollowAsync(slug, cancellationToken);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    [HttpDelete("{slug}/follow")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Unfollow(string slug, CancellationToken cancellationToken)
    {
        var result = await follows.UnfollowAsync(slug, cancellationToken);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    /// <summary>
    /// Reads the catalogue query off the request and pins the store, so the storefront grid keeps
    /// the open-ended <c>attr.</c> filters model binding cannot express.
    /// </summary>
    private ListingSearchRequest ReadSearchRequest(Guid storeId)
    {
        var query = Request.Query;
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in query)
        {
            if (pair.Key.StartsWith("attr.", StringComparison.Ordinal) && pair.Key.Length > 5)
            {
                attributes[pair.Key[5..]] = pair.Value.ToString();
            }
        }

        return new ListingSearchRequest
        {
            Q = query["q"],
            Category = query["category"],
            Region = query["region"],
            StoreId = storeId,
            PriceMin = Decimal(query["priceMin"]),
            PriceMax = Decimal(query["priceMax"]),
            Condition = query["condition"],
            Delivery = Bool(query["delivery"]),
            Sort = query["sort"],
            Page = Int(query["page"]) ?? 1,
            PageSize = Int(query["pageSize"]) ?? PageRequest.DefaultPageSize,
            Attributes = attributes
        };

        static decimal? Decimal(string? raw) =>
            decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

        static int? Int(string? raw) => int.TryParse(raw, out var value) ? value : null;

        static bool? Bool(string? raw) => bool.TryParse(raw, out var value) ? value : null;
    }
}
