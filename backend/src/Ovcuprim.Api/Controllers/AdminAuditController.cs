using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Admin;
using Ovcuprim.Application.Common;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// The audit trail, read-only.
/// </summary>
/// <remarks>
/// Admin-only (PD-7.4): payloads carry free-text moderation reasons about identifiable people.
/// There is deliberately no write, edit, delete or bulk-export route here — <c>AuditLog</c> is
/// append-only, and reading it is the only thing an operator may do with it.
/// </remarks>
[ApiController]
[Authorize(Policy = AuthenticationSetup.Policies.Admin)]
[Route("api/v1/admin/audit")]
[Produces("application/json")]
[EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
public sealed class AdminAuditController(IAdminAuditService audit) : ApiControllerBase
{
    /// <summary>
    /// Every filter maps onto an index that already exists: entity, actor, and a range over the
    /// descending CreatedAt index. <c>action</c> is matched as a prefix, so "store." selects every
    /// storefront decision.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AuditEntryDto>>> Query(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? action,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        AdminOk(await audit.QueryAsync(
            new AuditQuery
            {
                EntityType = entityType,
                EntityId = entityId,
                ActorUserId = actorUserId,
                Action = action,
                From = from,
                To = to
            },
            new PageRequest { Page = page, PageSize = pageSize },
            cancellationToken));
}
