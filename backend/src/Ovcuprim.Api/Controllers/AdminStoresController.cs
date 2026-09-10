using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Stores;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// Storefront moderation. Every decision writes an audit row naming the actor, and the two that
/// take something away from a seller require a reason.
/// </summary>
[ApiController]
[Authorize(Policy = AuthenticationSetup.Policies.Admin)]
[Route("api/v1/admin/stores")]
[Produces("application/json")]
[EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
public sealed class AdminStoresController(IStoreAdminService stores) : ApiControllerBase
{
    /// <summary>Applications awaiting a decision by default; pass a status to see the others.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<StoreAdminDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StoreAdminDto>>> GetQueue(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        AdminOk(await stores.GetQueueAsync(
            status, new PageRequest { Page = page, PageSize = pageSize }, cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        return AdminNoContent(await stores.ApproveAsync(id, cancellationToken));
    }

    /// <summary>
    /// Turns down an application. The record is withdrawn rather than parked in a rejected state,
    /// so the seller is free to correct it and apply again; the decision and its reason live on in
    /// the audit trail.
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Reject(
        Guid id, RejectStoreRequest request, CancellationToken cancellationToken)
    {
        return AdminNoContent(await stores.RejectAsync(id, request.Reason, cancellationToken));
    }

    /// <summary>
    /// Closes the shopfront. The owner's listings stay live — suspending a storefront is not the
    /// same as delisting their goods.
    /// </summary>
    [HttpPost("{id:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Suspend(
        Guid id, SuspendStoreRequest request, CancellationToken cancellationToken)
    {
        return AdminNoContent(await stores.SuspendAsync(id, request.Reason, cancellationToken));
    }

    [HttpPost("{id:guid}/reinstate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Reinstate(Guid id, CancellationToken cancellationToken)
    {
        return AdminNoContent(await stores.ReinstateAsync(id, cancellationToken));
    }

    [HttpPost("{id:guid}/verify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Verify(Guid id, CancellationToken cancellationToken)
    {
        return AdminNoContent(await stores.SetVerifiedAsync(id, verified: true, cancellationToken));
    }

    [HttpPost("{id:guid}/unverify")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Unverify(Guid id, CancellationToken cancellationToken)
    {
        return AdminNoContent(await stores.SetVerifiedAsync(id, verified: false, cancellationToken));
    }
}
