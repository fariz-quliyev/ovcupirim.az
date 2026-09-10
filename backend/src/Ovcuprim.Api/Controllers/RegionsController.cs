using Microsoft.AspNetCore.Mvc;
using Ovcuprim.Application.Regions;

namespace Ovcuprim.Api.Controllers;

public sealed class RegionsController(IRegionService regions) : ApiControllerBase
{
    /// <summary>Active regions, parents with their districts nested.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RegionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<ActionResult<IReadOnlyList<RegionDto>>> GetAll(CancellationToken cancellationToken)
    {
        var all = await regions.GetAllAsync(cancellationToken);
        return CachedOk(all, TimeSpan.FromHours(1));
    }

    /// <summary>Listing density per region, for the map discovery view.</summary>
    [HttpGet("stats")]
    [ProducesResponseType<IReadOnlyList<RegionStatDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RegionStatDto>>> GetStats(CancellationToken cancellationToken)
    {
        var stats = await regions.GetStatsAsync(cancellationToken);
        return CachedOk(stats, TimeSpan.FromMinutes(5));
    }
}
