using Microsoft.AspNetCore.Mvc;
using Ovcuprim.Application.Content;

namespace Ovcuprim.Api.Controllers;

public sealed class FaqController(IContentService content) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FaqCategoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<ActionResult<IReadOnlyList<FaqCategoryDto>>> Get(CancellationToken cancellationToken)
    {
        var faq = await content.GetFaqAsync(cancellationToken);
        return CachedOk(faq, TimeSpan.FromMinutes(15));
    }
}
