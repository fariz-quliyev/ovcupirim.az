using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Admin;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// The operations landing page: how much work is waiting, how long it has been waiting, and what
/// was decided in the last day. Moderator-visible, because it is the moderator's own workload.
/// </summary>
[ApiController]
[Authorize(Policy = AuthenticationSetup.Policies.Moderator)]
[Route("api/v1/admin/overview")]
[Produces("application/json")]
[EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
public sealed class AdminOverviewController(IAdminOverviewService overview) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<AdminOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminOverviewDto>> Get(CancellationToken cancellationToken) =>
        AdminOk(await overview.GetAsync(cancellationToken));
}
