using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Listings.Search;

namespace Ovcuprim.Api.Controllers;

/// <summary>The seller's own cabinet: "Mənim elanlarım" and "Elan limiti".</summary>
[ApiController]
[Authorize]
[Route("api/v1/me")]
[Produces("application/json")]
public sealed class MeListingsController(
    IListingService listings,
    IListingQuotaService quota,
    IFavoriteService favorites,
    ICurrentUser currentUser) : ApiControllerBase
{
    /// <summary>
    /// One bucket at a time, matching the tabs a seller sees: active, pending, rejected, expired,
    /// sold, draft. Omitting the bucket returns everything.
    /// </summary>
    [HttpGet("listings")]
    [ProducesResponseType<PagedResult<ListingSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ListingSummaryDto>>> GetMine(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        FromResult(await listings.GetMineAsync(
            status,
            new PageRequest { Page = page, PageSize = pageSize },
            cancellationToken));

    /// <summary>Per-category free-listing budget and, when exhausted, when the next slot opens.</summary>
    [HttpGet("listing-limits")]
    [ProducesResponseType<IReadOnlyList<ListingLimitDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ListingLimitDto>>> GetLimits(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Unauthorized();
        }

        return Ok(await quota.GetUsageAsync(userId, cancellationToken));
    }

    /// <summary>"Seçilmişlər" — saved listings, most recently saved first.</summary>
    [HttpGet("favorites")]
    [ProducesResponseType<PagedResult<ListingCardDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ListingCardDto>>> GetFavorites(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        FromResult(await favorites.GetMineAsync(
            new PageRequest { Page = page, PageSize = pageSize }, cancellationToken));

    /// <summary>Idempotent: saving an already-saved listing succeeds and changes nothing.</summary>
    [HttpPut("favorites/{shortId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> AddFavorite(long shortId, CancellationToken cancellationToken)
    {
        var result = await favorites.AddAsync(shortId, cancellationToken);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    [HttpDelete("favorites/{shortId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> RemoveFavorite(long shortId, CancellationToken cancellationToken)
    {
        var result = await favorites.RemoveAsync(shortId, cancellationToken);
        return result.Succeeded ? NoContent() : Problem(result);
    }
}
