using Microsoft.AspNetCore.Mvc;
using Ovcuprim.Application.Content;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Api.Controllers;

public sealed class PagesController(IContentService content) : ApiControllerBase
{
    /// <summary>Published informational or guide pages of one type.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StaticPageSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaticPageSummaryDto>>> List(
        [FromQuery] string type = "info",
        CancellationToken cancellationToken = default)
    {
        var pageType = string.Equals(type, "guide", StringComparison.OrdinalIgnoreCase)
            ? StaticPageType.Guide
            : StaticPageType.Info;

        var pages = await content.ListPagesAsync(pageType, cancellationToken);
        return CachedOk(pages, TimeSpan.FromMinutes(15));
    }

    /// <summary>One published page. An unpublished page is indistinguishable from a missing one.</summary>
    [HttpGet("{slug}")]
    [ProducesResponseType<StaticPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaticPageDto>> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var result = await content.GetPageAsync(slug, cancellationToken);

        return result.Succeeded && result.Value is not null
            ? CachedOk(result.Value, TimeSpan.FromMinutes(15))
            : Problem(result);
    }
}
