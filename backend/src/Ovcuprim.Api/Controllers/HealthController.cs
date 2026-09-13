using Microsoft.AspNetCore.Mvc;

namespace Ovcuprim.Api.Controllers;

/// <summary>Liveness probe. Readiness (including the database) is served from /health.</summary>
public sealed class HealthController : ApiControllerBase
{
    [HttpGet]
    public ActionResult<object> Get() => Ok(new
    {
        status = "ok",
        service = "ovcupirim-api",
        utc = DateTimeOffset.UtcNow
    });
}
