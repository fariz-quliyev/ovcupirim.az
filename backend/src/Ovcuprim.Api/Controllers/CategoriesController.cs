using Microsoft.AspNetCore.Mvc;
using Ovcuprim.Application.Categories;

namespace Ovcuprim.Api.Controllers;

/// <summary>Public taxonomy reads. Anonymous and cacheable; the tree changes rarely.</summary>
public sealed class CategoriesController(ICategoryService categories) : ApiControllerBase
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);

    /// <summary>The full active category tree.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryNodeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<ActionResult<IReadOnlyList<CategoryNodeDto>>> GetTree(CancellationToken cancellationToken)
    {
        var tree = await categories.GetTreeAsync(cancellationToken);
        return CachedOk(tree, CacheFor);
    }

    /// <summary>One category with its ancestors and children.</summary>
    [HttpGet("{slug}")]
    [ProducesResponseType<CategoryDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDetailDto>> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var result = await categories.GetBySlugAsync(slug, cancellationToken);

        return result.Succeeded && result.Value is not null
            ? CachedOk(result.Value, CacheFor)
            : Problem(result);
    }

    /// <summary>
    /// The effective attribute schema — own definitions plus those inherited from ancestors.
    /// This is the contract Phase 4 renders forms from and Phase 5 builds filters from.
    /// </summary>
    [HttpGet("{slug}/schema")]
    [ProducesResponseType<CategorySchemaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategorySchemaDto>> GetSchema(string slug, CancellationToken cancellationToken)
    {
        var result = await categories.GetSchemaAsync(slug, cancellationToken);

        return result.Succeeded && result.Value is not null
            ? CachedOk(result.Value, CacheFor)
            : Problem(result);
    }
}
