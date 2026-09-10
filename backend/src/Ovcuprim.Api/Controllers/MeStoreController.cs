using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Stores;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// The seller's own storefront. Every route resolves the store by owner, never by a supplied id,
/// so there is nothing here for one seller to point at another's shop.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/me/store")]
[Produces("application/json")]
public sealed class MeStoreController(
    IStoreService stores,
    IStoreMediaService media,
    IStoreFollowService follows) : ApiControllerBase
{
    /// <summary>The caller's store in any status, including one still awaiting approval.</summary>
    [HttpGet]
    [ProducesResponseType<StoreOwnerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StoreOwnerDto>> Get(CancellationToken cancellationToken) =>
        FromResult(await stores.GetMineAsync(cancellationToken));

    /// <summary>Applies for a storefront. It becomes public only once an administrator approves it.</summary>
    [HttpPost]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.StoreApply)]
    [ProducesResponseType<StoreOwnerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StoreOwnerDto>> Apply(
        ApplyForStoreRequest request, CancellationToken cancellationToken)
    {
        var result = await stores.ApplyAsync(request, cancellationToken);

        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Problem(result);
    }

    /// <summary>Edits the storefront. The name may change; the address it lives at may not.</summary>
    [HttpPut]
    [ProducesResponseType<StoreOwnerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StoreOwnerDto>> Update(
        UpdateStoreRequest request, CancellationToken cancellationToken) =>
        FromResult(await stores.UpdateMineAsync(request, cancellationToken));

    [HttpPost("logo")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.MediaUpload)]
    [RequestSizeLimit(StoreMediaService.MaxBytes + 8192)]
    [ProducesResponseType<StoreOwnerDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<StoreOwnerDto>> UploadLogo(IFormFile file, CancellationToken cancellationToken) =>
        UploadAsync(StoreImageKind.Logo, file, cancellationToken);

    [HttpPost("banner")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.MediaUpload)]
    [RequestSizeLimit(StoreMediaService.MaxBytes + 8192)]
    [ProducesResponseType<StoreOwnerDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<StoreOwnerDto>> UploadBanner(IFormFile file, CancellationToken cancellationToken) =>
        UploadAsync(StoreImageKind.Banner, file, cancellationToken);

    [HttpDelete("logo")]
    [ProducesResponseType<StoreOwnerDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreOwnerDto>> RemoveLogo(CancellationToken cancellationToken) =>
        FromResult(await media.RemoveAsync(StoreImageKind.Logo, cancellationToken));

    [HttpDelete("banner")]
    [ProducesResponseType<StoreOwnerDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StoreOwnerDto>> RemoveBanner(CancellationToken cancellationToken) =>
        FromResult(await media.RemoveAsync(StoreImageKind.Banner, cancellationToken));

    /// <summary>Storefronts this account follows.</summary>
    [HttpGet("/api/v1/me/followed-stores")]
    [ProducesResponseType<PagedResult<StoreCardDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StoreCardDto>>> GetFollowed(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        FromResult(await follows.GetMineAsync(
            new PageRequest { Page = page, PageSize = pageSize }, cancellationToken));

    private async Task<ActionResult<StoreOwnerDto>> UploadAsync(
        StoreImageKind kind, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["file"] = ["Fayl seçilməyib."] }));
        }

        await using var stream = file.OpenReadStream();

        var upload = new ListingMediaUpload(stream, file.FileName, file.ContentType, file.Length);

        return FromResult(await media.ReplaceAsync(kind, upload, cancellationToken));
    }
}
