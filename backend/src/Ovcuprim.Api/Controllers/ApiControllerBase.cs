using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ovcuprim.Application.Common;

namespace Ovcuprim.Api.Controllers;

/// <summary>Base for every API controller: fixes the version prefix and maps <see cref="Result"/> onto status codes.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<T> FromResult<T>(Result<T> result) =>
        result.Succeeded ? Ok(result.Value) : Problem(result);

    /// <summary>
    /// An administrative response. Never cached anywhere: these bodies carry queue contents,
    /// unmasked contact numbers and audit payloads, and none of it may sit in a browser cache or
    /// an intermediary after an operator signs out.
    /// </summary>
    protected ActionResult<T> AdminOk<T>(Result<T> result)
    {
        Response.Headers.CacheControl = "no-store";

        return result.Succeeded ? Ok(result.Value) : Problem(result);
    }

    /// <summary>The same header for a payload that is not a <see cref="Result{T}"/>.</summary>
    protected ActionResult<T> AdminOk<T>(T value)
    {
        Response.Headers.CacheControl = "no-store";

        return Ok(value);
    }

    /// <summary>An administrative action with no body.</summary>
    protected ActionResult AdminNoContent(Result result)
    {
        Response.Headers.CacheControl = "no-store";

        return result.Succeeded ? NoContent() : Problem(result);
    }

    /// <summary>
    /// Returns a cacheable payload with a content-derived ETag, answering 304 when the client
    /// already holds it. The hash is over the response body, so it stays correct across restarts
    /// and deployments — unlike a version counter held in memory.
    /// </summary>
    /// <param name="variesByUser">
    /// True when the body depends on who asked — a catalogue card carries the caller's own
    /// <c>isFavorited</c>, for instance. Such a response is marked <c>private</c> and varies on
    /// Authorization, because a shared cache would otherwise hand one visitor another visitor's
    /// saved state. Leave false only for a body that is identical for everyone.
    /// </param>
    protected ActionResult<T> CachedOk<T>(T value, TimeSpan maxAge, bool variesByUser = false)
    {
        var json = JsonSerializer.Serialize(value, CacheJsonOptions);
        var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..32]}\"";

        var scope = variesByUser ? "private" : "public";

        Response.Headers.CacheControl = $"{scope}, max-age={(int)maxAge.TotalSeconds}";
        Response.Headers.ETag = etag;

        if (variesByUser)
        {
            // Belt and braces: even a cache that ignores "private" must not reuse one caller's
            // response for another.
            Response.Headers.Vary = "Authorization";
        }

        if (Request.Headers.IfNoneMatch.Contains(etag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(value);
    }

    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);

    protected ActionResult Problem(Result result)
    {
        var (status, title) = result.Error switch
        {
            ResultError.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            ResultError.Validation => (StatusCodes.Status400BadRequest, "Validation failed"),
            ResultError.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ResultError.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            ResultError.Unauthorized => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ResultError.RateLimited => (StatusCodes.Status429TooManyRequests, "Too many requests"),
            ResultError.Unavailable => (StatusCodes.Status503ServiceUnavailable, "Service unavailable"),
            _ => (StatusCodes.Status400BadRequest, "Request failed")
        };

        if (result.FieldErrors is { Count: > 0 } fieldErrors)
        {
            // Same shape the ValidationFilter produces, so the client has one error contract.
            return ValidationProblem(new ValidationProblemDetails(fieldErrors.ToDictionary(e => e.Key, e => e.Value))
            {
                Status = status,
                Title = title,
                Detail = result.Message
            });
        }

        return Problem(statusCode: status, title: title, detail: result.Message);
    }
}
