using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Listings;
using Ovcuprim.Application.Common;
using Ovcuprim.Application.Listings.Search;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// Listing creation and lifecycle. Every mutating route resolves the listing by id and then checks
/// ownership on the row, so an id alone never grants access.
/// </summary>
[Authorize]
public sealed class ListingsController(
    IListingService listings,
    IListingPublishService publishing,
    IListingMediaService media,
    IListingSearchService search,
    IListingReportService reports) : ApiControllerBase
{
    /// <summary>
    /// The public catalogue and search. Only Active listings are ever returned, and the response is
    /// cacheable for a minute with a content ETag.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.ListingSearch)]
    [ProducesResponseType<ListingSearchResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ListingSearchResultDto>> Search(CancellationToken cancellationToken)
    {
        var result = await search.SearchAsync(ReadSearchRequest(), cancellationToken);

        // Each card carries the caller's own isFavorited, so this response is per-visitor.
        return result.Succeeded
            ? CachedOk(result.Value!, TimeSpan.FromMinutes(1), variesByUser: true)
            : Problem(result);
    }

    /// <summary>
    /// Category and region counts for the current filter set. Parsed by the same parser as the
    /// result page, so the two always describe the same query.
    /// </summary>
    [HttpGet("facets")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.ListingSearch)]
    [ProducesResponseType<ListingFacetsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ListingFacetsDto>> Facets(CancellationToken cancellationToken)
    {
        var result = await search.FacetsAsync(ReadSearchRequest(), cancellationToken);

        // Counts are the same for everyone, so this one stays shareable.
        return result.Succeeded ? CachedOk(result.Value!, TimeSpan.FromMinutes(1)) : Problem(result);
    }

    /// <summary>
    /// Reads the catalogue query straight off the request so dynamic attribute filters can arrive
    /// under an open-ended <c>attr.</c> prefix, which model binding cannot express.
    /// </summary>
    private ListingSearchRequest ReadSearchRequest()
    {
        var query = Request.Query;
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in query)
        {
            if (pair.Key.StartsWith("attr.", StringComparison.Ordinal) && pair.Key.Length > 5)
            {
                attributes[pair.Key[5..]] = pair.Value.ToString();
            }
        }

        return new ListingSearchRequest
        {
            Q = query["q"],
            Category = query["category"],
            Region = query["region"],
            PriceMin = Decimal(query["priceMin"]),
            PriceMax = Decimal(query["priceMax"]),
            Condition = query["condition"],
            Delivery = Bool(query["delivery"]),
            SellerType = query["sellerType"],
            Sort = query["sort"],
            Page = Int(query["page"]) ?? 1,
            PageSize = Int(query["pageSize"]) ?? PageRequest.DefaultPageSize,
            Attributes = attributes
        };

        static decimal? Decimal(string? raw) =>
            decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

        static int? Int(string? raw) => int.TryParse(raw, out var value) ? value : null;

        static bool? Bool(string? raw) => bool.TryParse(raw, out var value) ? value : null;
    }

    /// <summary>Creates the draft the wizard attaches images to.</summary>
    [HttpPost]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.ListingCreate)]
    [ProducesResponseType<ListingDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ListingDetailDto>> Create(
        CreateListingRequest request, CancellationToken cancellationToken)
    {
        var result = await listings.CreateDraftAsync(request, cancellationToken);

        if (!result.Succeeded)
        {
            return Problem(result);
        }

        return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ListingDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ListingDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await listings.GetAsync(id, cancellationToken));

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ListingDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ListingDetailDto>> Update(
        Guid id, UpdateListingRequest request, CancellationToken cancellationToken) =>
        FromResult(await listings.UpdateAsync(id, request, cancellationToken));

    /// <summary>Submits for moderation. Nothing goes live without a moderator.</summary>
    [HttpPost("{id:guid}/publish")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.ListingCreate)]
    [ProducesResponseType<ListingDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ListingDetailDto>> Publish(
        Guid id, PublishListingRequest request, CancellationToken cancellationToken) =>
        FromResult(await publishing.PublishAsync(id, request, cancellationToken));

    /// <summary>Brings an expired listing back inside the 30-day window and re-queues it.</summary>
    [HttpPost("{id:guid}/restore")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.ListingCreate)]
    [ProducesResponseType<ListingDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ListingDetailDto>> Restore(
        Guid id, PublishListingRequest request, CancellationToken cancellationToken) =>
        FromResult(await publishing.RestoreAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/mark-sold")]
    [ProducesResponseType<ListingDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ListingDetailDto>> MarkSold(Guid id, CancellationToken cancellationToken) =>
        FromResult(await listings.MarkSoldAsync(id, cancellationToken));

    /// <summary>
    /// Seller removal ("Elanı sil"). A draft is deleted; anything that has been submitted retires
    /// to "Müddəti başa çatmış" and stays restorable for 30 days.
    /// </summary>
    /// <remarks>
    /// Deliberately a POST and not a DELETE: for a published listing this is a state change, not a
    /// removal, and the seller can undo it. There is no seller-facing hard delete — permanent
    /// removal is an administrative action.
    /// </remarks>
    [HttpPost("{id:guid}/delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await listings.DeleteAsync(id, cancellationToken);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    [HttpPost("{id:guid}/media")]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.MediaUpload)]
    [RequestSizeLimit(ListingMediaService.MaxBytesPerImage + 8192)]
    [ProducesResponseType<ListingMediaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ListingMediaDto>> AddMedia(
        Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["file"] = ["Fayl seçilməyib."] }));
        }

        await using var stream = file.OpenReadStream();

        var upload = new ListingMediaUpload(stream, file.FileName, file.ContentType, file.Length);
        var result = await media.AddAsync(id, upload, cancellationToken);

        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, result.Value) : Problem(result);
    }

    [HttpDelete("{id:guid}/media/{mediaId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> RemoveMedia(Guid id, Guid mediaId, CancellationToken cancellationToken)
    {
        var result = await media.RemoveAsync(id, mediaId, cancellationToken);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    /// <summary>Replaces the whole order; the first image becomes the cover.</summary>
    [HttpPut("{id:guid}/media/order")]
    [ProducesResponseType<IReadOnlyList<ListingMediaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ListingMediaDto>>> ReorderMedia(
        Guid id, ReorderMediaRequest request, CancellationToken cancellationToken) =>
        FromResult(await media.ReorderAsync(id, request.MediaIds, cancellationToken));

    /// <summary>
    /// The buyer-facing page. Only an Active listing resolves; every other state answers 404 with
    /// the same wording, so a removed listing cannot be told apart from one that never existed.
    /// </summary>
    [HttpGet("by-short-id/{shortId:long}")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.ListingDetail)]
    [ProducesResponseType<ListingPublicDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ListingPublicDto>> GetPublic(
        long shortId, [FromQuery] bool ageConfirmed, CancellationToken cancellationToken) =>
        FromResult(await listings.GetPublicAsync(shortId, ageConfirmed, cancellationToken));

    /// <summary>"Bənzər elanlar" — same category, newest first.</summary>
    [HttpGet("by-short-id/{shortId:long}/similar")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.ListingDetail)]
    [ProducesResponseType<IReadOnlyList<ListingCardDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ListingCardDto>>> GetSimilar(
        long shortId, CancellationToken cancellationToken) =>
        FromResult(await search.SimilarAsync(shortId, cancellationToken));

    /// <summary>"Şikayət et". Accepted from signed-out visitors, rate-limited per client.</summary>
    [HttpPost("by-short-id/{shortId:long}/report")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.ListingReport)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Report(
        long shortId, SubmitReportRequest request, CancellationToken cancellationToken)
    {
        var result = await reports.SubmitAsync(shortId, request, cancellationToken);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    /// <summary>Revealed on demand, the way "Nömrəni göstər" works, so the number is not in the page payload.</summary>
    [HttpGet("by-short-id/{shortId:long}/phone")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.RateLimits.PhoneReveal)]
    [ProducesResponseType<ListingPhoneDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ListingPhoneDto>> GetPublicPhone(long shortId, CancellationToken cancellationToken) =>
        FromResult(await listings.GetPublicPhoneAsync(shortId, cancellationToken));
}
