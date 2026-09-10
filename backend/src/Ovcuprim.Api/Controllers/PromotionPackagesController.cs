using Microsoft.AspNetCore.Mvc;
using Ovcuprim.Application.Payments;

namespace Ovcuprim.Api.Controllers;

/// <summary>Public catalog reads. Anonymous and cacheable — the same treatment categories get.</summary>
[Route("api/v1/promotion-packages")]
public sealed class PromotionPackagesController(IPromotionPackageService packages) : ApiControllerBase
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);

    /// <summary>Active packages a seller can buy, in display order.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PromotionPackageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<ActionResult<IReadOnlyList<PromotionPackageDto>>> GetActive(CancellationToken cancellationToken)
    {
        var active = await packages.GetActiveAsync(cancellationToken);
        return CachedOk(active, CacheFor);
    }
}
