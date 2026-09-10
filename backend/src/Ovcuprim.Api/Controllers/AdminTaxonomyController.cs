using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ovcuprim.Api.Infrastructure;
using Ovcuprim.Application.Categories;
using Ovcuprim.Application.Regions;

namespace Ovcuprim.Api.Controllers;

/// <summary>
/// Taxonomy mutations. Every action requires the Admin role and writes an audit entry; the public
/// read endpoints stay anonymous and cached.
/// </summary>
[Authorize(Policy = AuthenticationSetup.Policies.Admin)]
[Route("api/v1/admin")]
[EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]
public sealed class AdminTaxonomyController(
    ICategoryAdminService categoryAdmin,
    IRegionService regions) : ApiControllerBase
{
    /// <summary>
    /// The whole tree, including the deactivated categories the public endpoint filters out —
    /// otherwise a category can be deactivated and then never seen or repaired again.
    /// </summary>
    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<AdminCategoryNodeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AdminCategoryNodeDto>>> GetCategories(
        CancellationToken cancellationToken) =>
        AdminOk(await categoryAdmin.GetAdminTreeAsync(cancellationToken));

    /// <summary>The attributes defined on one category, with the ids an editor needs to address.</summary>
    [HttpGet("categories/{id:int}/attributes")]
    [ProducesResponseType<IReadOnlyList<AdminAttributeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AdminAttributeDto>>> GetAttributes(
        int id, CancellationToken cancellationToken) =>
        AdminOk(await categoryAdmin.GetAttributesAsync(id, cancellationToken));

    [HttpPost("categories")]
    [ProducesResponseType<CategoryDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryDetailDto>> CreateCategory(
        CreateCategoryRequest request, CancellationToken cancellationToken) =>
        AdminOk(await categoryAdmin.CreateCategoryAsync(request, cancellationToken));

    [HttpPut("categories/{id:int}")]
    [ProducesResponseType<CategoryDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDetailDto>> UpdateCategory(
        int id, UpdateCategoryRequest request, CancellationToken cancellationToken) =>
        AdminOk(await categoryAdmin.UpdateCategoryAsync(id, request, cancellationToken));

    [HttpPost("categories/reorder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> ReorderCategories(ReorderRequest request, CancellationToken cancellationToken)
    {
        return AdminNoContent(await categoryAdmin.ReorderCategoriesAsync(request, cancellationToken));
    }

    /// <summary>Changing a restriction status is audited separately from an ordinary edit.</summary>
    [HttpPut("categories/{id:int}/restriction")]
    [ProducesResponseType<CategoryDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDetailDto>> SetRestriction(
        int id, SetRestrictionRequest request, CancellationToken cancellationToken) =>
        AdminOk(await categoryAdmin.SetRestrictionAsync(id, request, cancellationToken));

    [HttpDelete("categories/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteCategory(int id, CancellationToken cancellationToken)
    {
        return AdminNoContent(await categoryAdmin.DeleteCategoryAsync(id, cancellationToken));
    }

    [HttpPost("attributes")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<int>> CreateAttribute(
        CreateAttributeRequest request, CancellationToken cancellationToken) =>
        AdminOk(await categoryAdmin.CreateAttributeAsync(request, cancellationToken));

    [HttpDelete("attributes/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> DeleteAttribute(int id, CancellationToken cancellationToken)
    {
        return AdminNoContent(await categoryAdmin.DeleteAttributeAsync(id, cancellationToken));
    }

    [HttpPost("attributes/{attributeId:int}/options")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> CreateOption(
        int attributeId, CreateOptionRequest request, CancellationToken cancellationToken) =>
        AdminOk(await categoryAdmin.CreateOptionAsync(attributeId, request, cancellationToken));

    [HttpDelete("attributes/{attributeId:int}/options/{optionId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> DeleteOption(int attributeId, int optionId, CancellationToken cancellationToken)
    {
        return AdminNoContent(await categoryAdmin.DeleteOptionAsync(attributeId, optionId, cancellationToken));
    }

    /// <summary>
    /// The full administrative dataset, including records that never appear in the public picker.
    /// Used to administer locations and to verify an authoritative import.
    /// </summary>
    [HttpGet("regions")]
    [ProducesResponseType<IReadOnlyList<AdminRegionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AdminRegionDto>>> GetRegions(CancellationToken cancellationToken) =>
        AdminOk(await regions.GetAllForAdminAsync(cancellationToken));

    /// <summary>
    /// Bulk-loads the authoritative Azerbaijani administrative dataset. Idempotent: an existing
    /// slug is updated, not duplicated. Every row must state isSelectable explicitly.
    /// </summary>
    [HttpPost("regions/import")]
    [ProducesResponseType<RegionImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegionImportResult>> ImportRegions(
        RegionImportRequest request, CancellationToken cancellationToken) =>
        AdminOk(await regions.ImportAsync(request.Regions, cancellationToken));
}

public sealed record RegionImportRequest(IReadOnlyList<RegionImportRow> Regions);
