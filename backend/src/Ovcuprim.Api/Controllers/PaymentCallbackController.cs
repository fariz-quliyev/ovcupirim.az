using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Payments;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// The gateway's own server-to-server callback. Deliberately anonymous — the caller is Epoint, not a
/// signed-in user — and protected instead by signature verification inside
/// <see cref="IPaymentCallbackService"/> (docs/payment-integration-design.md, section F). The
/// callback body is never trusted as proof of payment on its own; it only triggers the authoritative
/// status re-check that actually decides whether a promotion activates.
/// </summary>
[ApiController]
[Route("api/v1/payments/callback")]
[Produces("application/json")]
public sealed class PaymentCallbackController(IPaymentCallbackService callbacks) : ApiControllerBase
{
    /// <summary>
    /// <paramref name="provider"/> identifies which gateway this callback is for in logs and
    /// routing — it does not by itself authorise anything; only a verified signature does.
    /// </summary>
    [HttpPost("{provider}")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.PaymentCallback)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> Receive(string provider, CancellationToken cancellationToken)
    {
        _ = provider;

        if (!Request.HasFormContentType)
        {
            return BadRequest();
        }

        var fields = Request.Form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.Ordinal);

        var result = await callbacks.ProcessCallbackAsync(fields, cancellationToken);

        // A bad signature is refused outright. A delivery the authoritative re-check could not decide
        // — the gateway's own status endpoint unreachable, or not yet reporting a final state
        // (integration audit H-1/L-5) — answers 503 so a gateway that retries on non-2xx comes back;
        // the expiry sweep re-polls such an order regardless. Everything else — processed,
        // already-processed, or a well-signed but unrecognised order — answers 200 so the gateway
        // stops retrying and nothing about whether a given reference exists is revealed.
        return result.Error switch
        {
            ResultError.Validation => BadRequest(),
            ResultError.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable),
            _ => Ok()
        };
    }
}
