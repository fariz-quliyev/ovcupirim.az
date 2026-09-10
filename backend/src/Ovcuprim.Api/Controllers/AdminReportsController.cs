using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// What visitors have flagged. Closing a report records who closed it; acting on the listing
/// itself goes through the moderation endpoints, so a listing's status only ever changes in one
/// place.
/// </summary>
[ApiController]
[Authorize(Policy = AuthenticationSetup.Policies.Moderator)]
[Route("api/v1/admin/reports")]
[Produces("application/json")]
[EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
public sealed class AdminReportsController(IListingReportService reports) : ApiControllerBase
{
    /// <summary>Open reports by default; pass a status to review what has already been closed.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ReportDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ReportDto>>> GetQueue(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        AdminOk(await reports.GetQueueAsync(
            status,
            new PageRequest { Page = page, PageSize = pageSize },
            cancellationToken));

    /// <summary>The report was justified and has been acted on.</summary>
    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Resolve(Guid id, CancellationToken cancellationToken)
    {
        return AdminNoContent(await reports.ResolveAsync(id, cancellationToken));
    }

    /// <summary>Nothing was wrong with the listing.</summary>
    [HttpPost("{id:guid}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Dismiss(Guid id, CancellationToken cancellationToken)
    {
        return AdminNoContent(await reports.DismissAsync(id, cancellationToken));
    }
}
