using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// Every endpoint here is anonymous by design — they are the entry points to authentication.
/// The authenticated current-user endpoint lives on <see cref="UsersController"/>.
/// </summary>
[EnableRateLimiting(AuthenticationSetup.RateLimits.Auth)]
public sealed class AuthController(IAuthService authService) : ApiControllerBase
{
    /// <summary>Starts registration and sends a one-time code. The response is identical for new and existing numbers.</summary>
    [HttpPost("register")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.OtpRequest)]
    [ProducesResponseType<OtpRequestResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OtpRequestResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        return FromResult(result);
    }

    /// <summary>Sends a login code. Returns the same body whether or not the number has an account.</summary>
    [HttpPost("login")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.OtpRequest)]
    [ProducesResponseType<OtpRequestResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OtpRequestResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RequestLoginOtpAsync(request, cancellationToken);
        return FromResult(result);
    }

    /// <summary>Re-sends a code, subject to the resend cooldown.</summary>
    [HttpPost("otp/resend")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.OtpRequest)]
    [ProducesResponseType<OtpRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OtpRequestResponse>> Resend(ResendOtpRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.ResendOtpAsync(request, cancellationToken);
        return FromResult(result);
    }

    /// <summary>Exchanges a valid code for an access token; the refresh token is set as an HttpOnly cookie.</summary>
    [HttpPost("verify")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.OtpVerify)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Verify(VerifyOtpRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.VerifyOtpAsync(request, ClientIp(), cancellationToken);

        return CompleteAuth(result);
    }

    /// <summary>
    /// Signs an administrator in with a password. No SMS is sent and no code is involved: an Admin
    /// account is excluded from the OTP flow entirely, so this is the only door to the panel.
    /// </summary>
    /// <remarks>
    /// Anonymous like the rest of this controller — it has to be, it is a sign-in — which is why it
    /// carries the tightest rate limit on the API alongside a per-account lockout, and why every
    /// rejection returns one identical message.
    /// </remarks>
    [HttpPost("admin/login")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.AdminLogin)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> AdminLogin(AdminLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.AdminPasswordLoginAsync(request, ClientIp(), cancellationToken);

        return CompleteAuth(result);
    }

    /// <summary>Rotates the refresh token cookie and issues a fresh access token.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        var token = RefreshTokenCookie.Read(Request);

        if (token is null)
        {
            return Problem(Result.Failure(ResultError.Unauthorized, "Sessiya tapılmadı."));
        }

        var result = await authService.RefreshAsync(token, ClientIp(), cancellationToken);

        if (!result.Succeeded)
        {
            // A rejected refresh token is worthless — drop it so the browser stops sending it.
            RefreshTokenCookie.Clear(Response);
        }

        return CompleteAuth(result);
    }

    /// <summary>Revokes the presented refresh token and clears the cookie.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Logout(CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(RefreshTokenCookie.Read(Request), cancellationToken);
        RefreshTokenCookie.Clear(Response);

        return NoContent();
    }

    private ActionResult<AuthResponse> CompleteAuth(Result<AuthTokens> result)
    {
        if (!result.Succeeded || result.Value is null)
        {
            return Problem(result);
        }

        RefreshTokenCookie.Write(Response, result.Value.RefreshToken, result.Value.RefreshTokenExpiresAt);

        return Ok(result.Value.Response);
    }

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
