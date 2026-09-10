using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// The moderation queue. Every decision writes an append-only ModerationAction plus an AuditLog
/// row, so who approved what is answerable after the fact.
/// </summary>
[ApiController]
[Authorize(Policy = AuthenticationSetup.Policies.Moderator)]
[Route("api/v1/admin/moderation")]
[Produces("application/json")]
[EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
public sealed class AdminModerationController(IListingModerationService moderation) : ApiControllerBase
{
    /// <summary>
    /// The moderation queue. <c>status=pending</c> (the default) is the ordinary approve/reject
    /// queue; <c>status=active</c> is the smaller set of live listings a moderator can still block.
    /// <c>strict=true</c> narrows either one to restricted and unclassified categories —
    /// unclassified means the classification is still pending, not that the goods are restricted.
    /// </summary>
    [HttpGet("queue")]
    [ProducesResponseType<PagedResult<ModerationQueueItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ModerationQueueItemDto>>> GetQueue(
        [FromQuery] bool? strict,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        AdminOk(await moderation.GetQueueAsync(
            strict,
            new PageRequest { Page = page, PageSize = pageSize },
            status,
            cancellationToken));

    /// <summary>
    /// The whole listing as a moderator needs to see it — images, description, attributes, decision
    /// history, and the seller's contact number unmasked (PD-7.5). The ordinary listing endpoint is
    /// owner-scoped, so this is the only route that shows a moderator what they are deciding on.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ModerationDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ModerationDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        AdminOk(await moderation.GetForModerationAsync(id, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        return AdminNoContent(await moderation.ApproveAsync(id, cancellationToken));
    }

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Reject(
        Guid id, RejectListingRequest request, CancellationToken cancellationToken)
    {
        return AdminNoContent(await moderation.RejectAsync(id, request.Reason, cancellationToken));
    }

    [HttpPost("{id:guid}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Block(
        Guid id, BlockListingRequest request, CancellationToken cancellationToken)
    {
        return AdminNoContent(await moderation.BlockAsync(id, request.Reason, cancellationToken));
    }

    [HttpPost("{id:guid}/unblock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Unblock(Guid id, CancellationToken cancellationToken)
    {
        return AdminNoContent(await moderation.UnblockAsync(id, cancellationToken));
    }
}
