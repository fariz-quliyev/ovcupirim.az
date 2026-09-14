using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Users;

namespace Ovcuprim.Api.Controllers;

[Authorize]
public sealed class UsersController(
    IUserService userService,
    IAuthService authService,
    ICurrentUser currentUser) : ApiControllerBase
{
    /// <summary>The caller's own profile.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Problem(Result.Failure(ResultError.Unauthorized, "Giriş tələb olunur."));
        }

        var result = await userService.GetByIdAsync(userId, cancellationToken);
        return FromResult(result);
    }

    /// <summary>Updates the caller's own profile. The user id comes from the token, never from the body.</summary>
    [HttpPut("me")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> UpdateMe(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Problem(Result.Failure(ResultError.Unauthorized, "Giriş tələb olunur."));
        }

        var result = await userService.UpdateProfileAsync(userId, request, cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Changes the caller's own administrator password. Admin-only, because no other account has a
    /// password at all.
    /// </summary>
    /// <remarks>
    /// Succeeds with 204 and ends every session the account had, this one included — so the browser
    /// signs in again straight afterwards. The refresh cookie is cleared here to match: leaving a
    /// revoked token in the browser only produces a confusing 401 on the next page load.
    /// </remarks>
    [HttpPost("me/password")]
    [Authorize(Policy = AuthenticationSetup.Policies.Admin)]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Problem(Result.Failure(ResultError.Unauthorized, "Giriş tələb olunur."));
        }

        var result = await authService.ChangePasswordAsync(
            userId, request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (result.Succeeded)
        {
            RefreshTokenCookie.Clear(Response);
        }

        return AdminNoContent(result);
    }

    /// <summary>Administrative user listing — the first role-gated endpoint, extended in Phase 7.</summary>
    [HttpGet]
    [Authorize(Policy = AuthenticationSetup.Policies.Admin)]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
    [ProducesResponseType<PagedResult<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<UserDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await userService.SearchAsync(q, new PageRequest { Page = page, PageSize = pageSize }, cancellationToken);
        return AdminOk(result);
    }

    /// <summary>One account, for the inspect view. Admin only; no mutation beyond the role verbs.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.Policies.Admin)]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken cancellationToken) =>
        AdminOk(await userService.GetByIdAsync(id, cancellationToken));

    /// <summary>
    /// Grants the Moderator role (PD-7.2). Admin is deliberately not grantable through the API, and
    /// an administrator cannot change their own role.
    /// </summary>
    [HttpPost("{id:guid}/moderator")]
    [Authorize(Policy = AuthenticationSetup.Policies.Admin)]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDto>> GrantModerator(Guid id, CancellationToken cancellationToken) =>
        AdminOk(await userService.SetModeratorAsync(id, isModerator: true, cancellationToken));

    /// <summary>Revokes the Moderator role, returning the account to an ordinary user.</summary>
    [HttpDelete("{id:guid}/moderator")]
    [Authorize(Policy = AuthenticationSetup.Policies.Admin)]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDto>> RevokeModerator(Guid id, CancellationToken cancellationToken) =>
        AdminOk(await userService.SetModeratorAsync(id, isModerator: false, cancellationToken));
}
