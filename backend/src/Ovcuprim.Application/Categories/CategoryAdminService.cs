using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Categories;

public sealed record CreateCategoryRequest(
    string NameAz,
    string? NameRu,
    int? ParentId,
    string? Slug,
    string? IconKey,
    string? DescriptionAz,
    int SortOrder);

public sealed record UpdateCategoryRequest(
    string NameAz,
    string? NameRu,
    string? DescriptionAz,
    string? MetaTitleAz,
    string? MetaDescriptionAz,
    string? IconKey,
    string? ImageKey,
    int SortOrder,
    bool IsActive,
    bool IsSelectable);

public sealed record ReorderRequest(IReadOnlyList<ReorderItem> Items);

public sealed record ReorderItem(int Id, int SortOrder);

public sealed record SetRestrictionRequest(string RestrictionStatus);

public sealed record CreateAttributeRequest(
    int CategoryId,
    string Key,
    string LabelAz,
    string DataType,
    string? Unit,
    string? PlaceholderAz,
    bool IsRequired,
    bool IsFilterable,
    bool IsSearchable,
    bool AppliesToDescendants,
    decimal? MinValue,
    decimal? MaxValue,
    int? DecimalPlaces,
    int? MaxLength,
    int SortOrder);

public sealed record CreateOptionRequest(string Value, string LabelAz, string? LabelRu, int SortOrder);

/// <summary>
/// A category as an administrator needs to see it — including the deactivated ones the public tree
/// filters out, which is otherwise how a category becomes unreachable and unrepairable.
/// </summary>
public sealed record AdminCategoryNodeDto(
    int Id,
    int? ParentId,
    string Slug,
    string NameAz,
    string? NameRu,
    /// <summary>
    /// The descriptive and SEO fields travel with the tree because <see cref="UpdateCategoryRequest"/>
    /// assigns every one of them: an editor that cannot read them back cannot send them back, and
    /// would blank them on the first save.
    /// </summary>
    string? DescriptionAz,
    string? MetaTitleAz,
    string? MetaDescriptionAz,
    string? IconKey,
    string? ImageKey,
    int SortOrder,
    int Depth,
    bool IsActive,
    bool IsSelectable,
    bool IsLeaf,
    string RestrictionStatus,
    int ListingCount,
    int AttributeCount,
    IReadOnlyList<AdminCategoryNodeDto> Children);

/// <summary>One option of a Select or MultiSelect attribute, with the id an editor needs.</summary>
public sealed record AdminAttributeOptionDto(int Id, string Value, string LabelAz, int SortOrder, bool IsActive);

/// <summary>
/// An attribute as an administrator edits it.
/// </summary>
/// <remarks>
/// Separate from the public <c>AttributeSchema</c> on purpose: that contract is consumed by the
/// listing form and the filter panel and carries no database identifiers, and it is not going to
/// grow one just so an admin screen can address a row.
/// </remarks>
public sealed record AdminAttributeDto(
    int Id,
    int CategoryId,
    string Key,
    string LabelAz,
    string? LabelRu,
    string DataType,
    string? Unit,
    bool IsRequired,
    bool IsFilterable,
    bool IsActive,
    bool AppliesToDescendants,
    int SortOrder,
    IReadOnlyList<AdminAttributeOptionDto> Options);

public interface ICategoryAdminService
{
    /// <summary>The whole tree, active and inactive alike, with attribute counts.</summary>
    Task<IReadOnlyList<AdminCategoryNodeDto>> GetAdminTreeAsync(CancellationToken cancellationToken = default);

    /// <summary>The attributes defined directly on one category, with their options.</summary>
    Task<Result<IReadOnlyList<AdminAttributeDto>>> GetAttributesAsync(
        int categoryId, CancellationToken cancellationToken = default);

    Task<Result<CategoryDetailDto>> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<Result<CategoryDetailDto>> UpdateCategoryAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken = default);
    Task<Result> ReorderCategoriesAsync(ReorderRequest request, CancellationToken cancellationToken = default);
    Task<Result<CategoryDetailDto>> SetRestrictionAsync(int id, SetRestrictionRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteCategoryAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<int>> CreateAttributeAsync(CreateAttributeRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAttributeAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<int>> CreateOptionAsync(int attributeId, CreateOptionRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteOptionAsync(int attributeId, int optionId, CancellationToken cancellationToken = default);
}

public sealed class CategoryAdminService(
    IAppDbContext db,
    ICategoryService categories,
    ITaxonomyCache cache,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : ICategoryAdminService
{
    private const int MaxDepth = 1;

    public async Task<Result<CategoryDetailDto>> CreateCategoryAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? AzerbaijaniText.ToSlug(request.NameAz)
            : request.Slug.Trim();

        if (!AzerbaijaniText.IsSlug(slug))
        {
            return Result<CategoryDetailDto>.Failure(ResultError.Validation, "Slug düzgün deyil.");
        }

        Category? parent = null;

        if (request.ParentId is { } parentId)
        {
            parent = await db.Categories.FirstOrDefaultAsync(c => c.Id == parentId, cancellationToken);

            if (parent is null)
            {
                return Result<CategoryDetailDto>.Failure(ResultError.Validation, "Valideyn kateqoriya tapılmadı.");
            }

            if (parent.Depth >= MaxDepth)
            {
                return Result<CategoryDetailDto>.Failure(
                    ResultError.Validation, "Kateqoriya iyerarxiyası iki səviyyə ilə məhdudlaşır.");
            }
        }

        var duplicate = await db.Categories
            .AnyAsync(c => c.Slug == slug && c.ParentId == request.ParentId, cancellationToken);

        if (duplicate)
        {
            return Result<CategoryDetailDto>.Conflict("Bu slug artıq istifadə olunur.");
        }

        var category = new Category
        {
            ParentId = parent?.Id,
            Depth = parent is null ? 0 : parent.Depth + 1,
            Slug = slug,
            NameAz = request.NameAz.Trim(),
            NameRu = request.NameRu,
            DescriptionAz = request.DescriptionAz,
            IconKey = request.IconKey,
            SortOrder = request.SortOrder,
            // Inherit the parent's status; a new top-level category starts unclassified.
            RestrictionStatus = parent?.RestrictionStatus ?? RestrictionStatus.Unclassified,
            CreatedAt = clock.UtcNow
        };

        db.Categories.Add(category);
        await AuditAsync("CategoryCreated", nameof(Category), category.Slug, new { category.Slug, category.NameAz }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        return await categories.GetBySlugAsync(category.Slug, cancellationToken);
    }

    public async Task<Result<CategoryDetailDto>> UpdateCategoryAsync(
        int id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            return Result<CategoryDetailDto>.NotFound("Kateqoriya tapılmadı.");
        }

        category.NameAz = request.NameAz.Trim();
        category.NameRu = request.NameRu;
        category.DescriptionAz = request.DescriptionAz;
        category.MetaTitleAz = request.MetaTitleAz;
        category.MetaDescriptionAz = request.MetaDescriptionAz;
        category.IconKey = request.IconKey;
        category.ImageKey = request.ImageKey;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        category.IsSelectable = request.IsSelectable;
        category.UpdatedAt = clock.UtcNow;

        // Deactivating a parent hides its children too.
        if (!request.IsActive)
        {
            var children = await db.Categories.Where(c => c.ParentId == category.Id).ToListAsync(cancellationToken);

            foreach (var child in children)
            {
                child.IsActive = false;
                child.UpdatedAt = clock.UtcNow;
            }
        }

        await AuditAsync("CategoryUpdated", nameof(Category), category.Slug, new { category.Slug, request.IsActive }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        return await categories.GetBySlugAsync(category.Slug, cancellationToken);
    }

    public async Task<Result> ReorderCategoriesAsync(ReorderRequest request, CancellationToken cancellationToken = default)
    {
        var ids = request.Items.Select(i => i.Id).ToList();
        var found = await db.Categories.Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken);

        if (found.Count != ids.Count)
        {
            return Result.NotFound("Bəzi kateqoriyalar tapılmadı.");
        }

        foreach (var category in found)
        {
            category.SortOrder = request.Items.First(i => i.Id == category.Id).SortOrder;
            category.UpdatedAt = clock.UtcNow;
        }

        await AuditAsync("CategoriesReordered", nameof(Category), string.Join(",", ids), new { ids }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        return Result.Success();
    }

    public async Task<Result<CategoryDetailDto>> SetRestrictionAsync(
        int id,
        SetRestrictionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<RestrictionStatus>(request.RestrictionStatus, ignoreCase: true, out var status))
        {
            return Result<CategoryDetailDto>.Failure(ResultError.Validation, "Naməlum məhdudiyyət statusu.");
        }

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            return Result<CategoryDetailDto>.NotFound("Kateqoriya tapılmadı.");
        }

        var previous = category.RestrictionStatus;
        category.RestrictionStatus = status;
        category.UpdatedAt = clock.UtcNow;

        // Restriction changes are audited separately from ordinary edits.
        await AuditAsync(
            "CategoryRestrictionChanged",
            nameof(Category),
            category.Slug,
            new { category.Slug, from = previous.ToString(), to = status.ToString() },
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        return await categories.GetBySlugAsync(category.Slug, cancellationToken);
    }

    public async Task<Result> DeleteCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            return Result.NotFound("Kateqoriya tapılmadı.");
        }

        if (category.Children.Count > 0)
        {
            return Result.Conflict("Alt kateqoriyası olan kateqoriya silinə bilməz. Əvvəlcə onları silin.");
        }

        if (category.ListingCount > 0)
        {
            return Result.Conflict("Elanı olan kateqoriya silinə bilməz. Onu deaktiv edin.");
        }

        db.Categories.Remove(category);
        await AuditAsync("CategoryDeleted", nameof(Category), category.Slug, new { category.Slug }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        return Result.Success();
    }

    public async Task<Result<int>> CreateAttributeAsync(
        CreateAttributeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<AttributeDataType>(request.DataType, ignoreCase: true, out var dataType))
        {
            return Result<int>.Failure(ResultError.Validation, "Naməlum xüsusiyyət tipi.");
        }

        if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            return Result<int>.Failure(ResultError.Validation, "Kateqoriya tapılmadı.");
        }

        var key = request.Key.Trim();

        if (!AzerbaijaniText.IsSlug(key.Replace('_', '-')))
        {
            return Result<int>.Failure(ResultError.Validation, "Açar yalnız kiçik hərf, rəqəm və alt xətdən ibarət ola bilər.");
        }

        if (await db.AttributeDefinitions.AnyAsync(a => a.CategoryId == request.CategoryId && a.Key == key, cancellationToken))
        {
            return Result<int>.Conflict("Bu kateqoriyada belə açar artıq var.");
        }

        var definition = new AttributeDefinition
        {
            CategoryId = request.CategoryId,
            Key = key,
            LabelAz = request.LabelAz.Trim(),
            DataType = dataType,
            Unit = request.Unit,
            PlaceholderAz = request.PlaceholderAz,
            IsRequired = request.IsRequired,
            IsFilterable = request.IsFilterable,
            IsSearchable = request.IsSearchable,
            AppliesToDescendants = request.AppliesToDescendants,
            MinValue = request.MinValue,
            MaxValue = request.MaxValue,
            DecimalPlaces = request.DecimalPlaces,
            MaxLength = request.MaxLength,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = clock.UtcNow
        };

        db.AttributeDefinitions.Add(definition);
        await db.SaveChangesAsync(cancellationToken);

        await AuditAsync("AttributeCreated", nameof(AttributeDefinition), definition.Id.ToString(),
            new { definition.Key, definition.CategoryId, dataType = dataType.ToString(), definition.IsFilterable },
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        // A new filterable Number attribute needs an expression index; index creation is a
        // maintenance operation, never done inline in a request.
        return Result<int>.Success(definition.Id);
    }

    public async Task<Result> DeleteAttributeAsync(int id, CancellationToken cancellationToken = default)
    {
        var definition = await db.AttributeDefinitions.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (definition is null)
        {
            return Result.NotFound("Xüsusiyyət tapılmadı.");
        }

        db.AttributeDefinitions.Remove(definition);
        await AuditAsync("AttributeDeleted", nameof(AttributeDefinition), id.ToString(), new { definition.Key }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        return Result.Success();
    }

    public async Task<Result<int>> CreateOptionAsync(
        int attributeId,
        CreateOptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await db.AttributeDefinitions.FirstOrDefaultAsync(a => a.Id == attributeId, cancellationToken);

        if (definition is null)
        {
            return Result<int>.NotFound("Xüsusiyyət tapılmadı.");
        }

        if (definition.DataType is not (AttributeDataType.Select or AttributeDataType.MultiSelect))
        {
            return Result<int>.Failure(ResultError.Validation, "Yalnız Select və MultiSelect tipləri dəyər siyahısı saxlayır.");
        }

        var value = request.Value.Trim();

        if (!AzerbaijaniText.IsSlug(value))
        {
            return Result<int>.Failure(ResultError.Validation, "Dəyər yalnız kiçik hərf, rəqəm və defisdən ibarət ola bilər.");
        }

        if (await db.AttributeOptions.AnyAsync(o => o.AttributeDefinitionId == attributeId && o.Value == value, cancellationToken))
        {
            return Result<int>.Conflict("Bu dəyər artıq mövcuddur.");
        }

        var option = new AttributeOption
        {
            AttributeDefinitionId = attributeId,
            Value = value,
            LabelAz = request.LabelAz.Trim(),
            LabelRu = request.LabelRu,
            SortOrder = request.SortOrder,
            IsActive = true
        };

        db.AttributeOptions.Add(option);
        await db.SaveChangesAsync(cancellationToken);

        await AuditAsync("AttributeOptionCreated", nameof(AttributeOption), option.Id.ToString(),
            new { option.Value, attributeId }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        return Result<int>.Success(option.Id);
    }

    public async Task<Result> DeleteOptionAsync(int attributeId, int optionId, CancellationToken cancellationToken = default)
    {
        var option = await db.AttributeOptions
            .FirstOrDefaultAsync(o => o.Id == optionId && o.AttributeDefinitionId == attributeId, cancellationToken);

        if (option is null)
        {
            return Result.NotFound("Dəyər tapılmadı.");
        }

        db.AttributeOptions.Remove(option);
        await AuditAsync("AttributeOptionDeleted", nameof(AttributeOption), optionId.ToString(), new { option.Value }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        cache.Invalidate();

        return Result.Success();
    }

    public async Task<IReadOnlyList<AdminCategoryNodeDto>> GetAdminTreeAsync(
        CancellationToken cancellationToken = default)
    {
        // Read straight through rather than from the taxonomy cache: the cached tree is the public
        // one, which hides exactly the rows this screen exists to repair.
        var all = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);

        var attributeCounts = await db.AttributeDefinitions.AsNoTracking()
            .GroupBy(a => a.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CategoryId, g => g.Count, cancellationToken);

        return Build(null);

        List<AdminCategoryNodeDto> Build(int? parentId) =>
        [
            .. all.Where(c => c.ParentId == parentId)
                .OrderBy(c => c.SortOrder)
                .Select(c =>
                {
                    var children = Build(c.Id);

                    return new AdminCategoryNodeDto(
                        c.Id,
                        c.ParentId,
                        c.Slug,
                        c.NameAz,
                        c.NameRu,
                        c.DescriptionAz,
                        c.MetaTitleAz,
                        c.MetaDescriptionAz,
                        c.IconKey,
                        c.ImageKey,
                        c.SortOrder,
                        c.Depth,
                        c.IsActive,
                        c.IsSelectable,
                        children.Count == 0,
                        c.RestrictionStatus.ToString(),
                        c.ListingCount,
                        attributeCounts.GetValueOrDefault(c.Id),
                        children);
                })
        ];
    }

    public async Task<Result<IReadOnlyList<AdminAttributeDto>>> GetAttributesAsync(
        int categoryId, CancellationToken cancellationToken = default)
    {
        if (!await db.Categories.AsNoTracking().AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            return Result<IReadOnlyList<AdminAttributeDto>>.NotFound("Kateqoriya tapılmadı.");
        }

        var attributes = await db.AttributeDefinitions.AsNoTracking()
            .Where(a => a.CategoryId == categoryId)
            .OrderBy(a => a.SortOrder)
            .Select(a => new AdminAttributeDto(
                a.Id,
                a.CategoryId,
                a.Key,
                a.LabelAz,
                a.LabelRu,
                a.DataType.ToString(),
                a.Unit,
                a.IsRequired,
                a.IsFilterable,
                a.IsActive,
                a.AppliesToDescendants,
                a.SortOrder,
                a.Options
                    .OrderBy(o => o.SortOrder)
                    .Select(o => new AdminAttributeOptionDto(o.Id, o.Value, o.LabelAz, o.SortOrder, o.IsActive))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AdminAttributeDto>>.Success(attributes);
    }

    private Task AuditAsync(string action, string entityType, string entityId, object payload, CancellationToken cancellationToken)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = currentUser.UserId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            PayloadJson = AuditPayload.From(payload),
            CreatedAt = clock.UtcNow
        });

        return Task.CompletedTask;
    }
}
