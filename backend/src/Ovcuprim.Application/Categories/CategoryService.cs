using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Categories;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryNodeDto>> GetTreeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The category named by the slug plus every active descendant, or null when no such category
    /// exists. This is what "filtering by a category" means everywhere on the site: choosing a
    /// parent includes everything beneath it, because listings only ever sit in leaves.
    /// </summary>
    Task<int[]?> GetSubtreeIdsAsync(string slug, CancellationToken cancellationToken = default);

    Task<Result<CategoryDetailDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Effective schema: the category's own attributes plus those inherited from ancestors.</summary>
    Task<Result<CategorySchemaDto>> GetSchemaAsync(string slug, CancellationToken cancellationToken = default);
}

public sealed class CategoryService(IAppDbContext db, ITaxonomyCache cache) : ICategoryService
{
    private const string TreeCacheKey = "categories:tree";

    public async Task<IReadOnlyList<CategoryNodeDto>> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        // One flat query behind the cache; the tree is assembled in memory. Children are never lazy-loaded.
        var categories = await LoadAllAsync(cancellationToken);

        return await cache.GetOrCreateAsync(
            TreeCacheKey,
            _ => Task.FromResult((IReadOnlyList<CategoryNodeDto>)BuildTree(categories)),
            cancellationToken);
    }

    public async Task<int[]?> GetSubtreeIdsAsync(string slug, CancellationToken cancellationToken = default)
    {
        var node = Find(await GetTreeAsync(cancellationToken), slug);

        if (node is null)
        {
            return null;
        }

        var ids = new List<int>();
        Collect(node, ids);

        return [.. ids];

        static CategoryNodeDto? Find(IReadOnlyList<CategoryNodeDto> nodes, string slug)
        {
            foreach (var node in nodes)
            {
                if (node.Slug == slug)
                {
                    return node;
                }

                if (Find(node.Children, slug) is { } match)
                {
                    return match;
                }
            }

            return null;
        }

        static void Collect(CategoryNodeDto node, List<int> ids)
        {
            ids.Add(node.Id);

            foreach (var child in node.Children)
            {
                Collect(child, ids);
            }
        }
    }

    public async Task<Result<CategoryDetailDto>> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var categories = await LoadAllAsync(cancellationToken);
        var category = categories.FirstOrDefault(c => c.Slug == slug && c.IsActive);

        if (category is null)
        {
            return Result<CategoryDetailDto>.NotFound("Kateqoriya tapılmadı.");
        }

        var byId = categories.ToDictionary(c => c.Id);
        var effective = EffectiveRestriction(category, byId);

        var children = categories
            .Where(c => c.ParentId == category.Id && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => ToNode(c, categories, byId))
            .ToList();

        return Result<CategoryDetailDto>.Success(new CategoryDetailDto(
            category.Id,
            category.Slug,
            category.NameAz,
            category.NameRu,
            category.DescriptionAz,
            category.MetaTitleAz,
            category.MetaDescriptionAz,
            category.IconKey,
            category.ImageKey,
            category.ListingCount,
            effective.ToString(),
            RequiresAgeConfirmation(effective),
            category.IsSelectable,
            BuildPath(category, byId),
            children));
    }

    public async Task<Result<CategorySchemaDto>> GetSchemaAsync(string slug, CancellationToken cancellationToken = default)
    {
        var categories = await LoadAllAsync(cancellationToken);
        var category = categories.FirstOrDefault(c => c.Slug == slug && c.IsActive);

        if (category is null)
        {
            return Result<CategorySchemaDto>.NotFound("Kateqoriya tapılmadı.");
        }

        var byId = categories.ToDictionary(c => c.Id);
        var chain = AncestorChain(category, byId);
        var chainIds = chain.Select(c => c.Id).ToList();

        var definitions = await db.AttributeDefinitions
            .AsNoTracking()
            .Where(a => chainIds.Contains(a.CategoryId) && a.IsActive)
            .Include(a => a.Options.Where(o => o.IsActive).OrderBy(o => o.SortOrder))
            .ToListAsync(cancellationToken);

        var effective = ResolveAttributes(definitions, category.Id, chain);
        var restriction = EffectiveRestriction(category, byId);
        var isLeaf = !categories.Any(c => c.ParentId == category.Id && c.IsActive);

        return Result<CategorySchemaDto>.Success(new CategorySchemaDto(
            new CategorySchemaCategoryDto(
                category.Id,
                category.Slug,
                category.NameAz,
                BuildPath(category, byId),
                restriction.ToString(),
                RequiresAgeConfirmation(restriction),
                category.IsSelectable,
                isLeaf),
            effective));
    }

    /// <summary>
    /// Ancestors first, then the category's own definitions. A definition on the category itself
    /// replaces an inherited one with the same key, and an ancestor definition is only inherited
    /// when it opts in through <see cref="AttributeDefinition.AppliesToDescendants"/>.
    /// </summary>
    private static List<AttributeSchemaDto> ResolveAttributes(
        List<AttributeDefinition> definitions,
        int categoryId,
        List<Category> chain)
    {
        var depthById = chain.Select((c, index) => (c.Id, Index: index)).ToDictionary(x => x.Id, x => x.Index);
        var resolved = new Dictionary<string, (AttributeDefinition Definition, bool Inherited)>(StringComparer.Ordinal);

        foreach (var definition in definitions
                     .Where(d => d.CategoryId == categoryId || d.AppliesToDescendants)
                     .OrderBy(d => depthById.GetValueOrDefault(d.CategoryId, int.MaxValue)))
        {
            var inherited = definition.CategoryId != categoryId;

            // Later entries are closer to the category, so an own definition overwrites an inherited one.
            resolved[definition.Key] = (definition, inherited);
        }

        return resolved.Values
            .OrderBy(x => x.Definition.SortOrder)
            .ThenBy(x => x.Definition.Key, StringComparer.Ordinal)
            .Select(x => ToSchema(x.Definition, x.Inherited))
            .ToList();
    }

    private static AttributeSchemaDto ToSchema(AttributeDefinition definition, bool inherited)
    {
        var hasOptions = definition.DataType is AttributeDataType.Select or AttributeDataType.MultiSelect;

        return new AttributeSchemaDto(
            definition.Key,
            definition.LabelAz,
            definition.LabelRu,
            definition.DataType.ToString(),
            definition.Unit,
            definition.IsRequired,
            definition.IsFilterable,
            definition.IsSearchable,
            definition.MinValue,
            definition.MaxValue,
            definition.DecimalPlaces,
            definition.MaxLength,
            definition.PlaceholderAz,
            definition.HelpTextAz,
            inherited,
            definition.SortOrder,
            hasOptions
                ? definition.Options
                    .OrderBy(o => o.SortOrder)
                    .Select(o => new AttributeOptionDto(o.Value, o.LabelAz, o.LabelRu))
                    .ToList()
                : null);
    }

    /// <summary>Root first, category last.</summary>
    private static List<Category> AncestorChain(Category category, Dictionary<int, Category> byId)
    {
        var chain = new List<Category>();
        var current = category;

        while (current is not null)
        {
            chain.Insert(0, current);
            current = current.ParentId is { } parentId ? byId.GetValueOrDefault(parentId) : null;
        }

        return chain;
    }

    /// <summary>
    /// The most severe status across the chain wins: Unrestricted &lt; Unclassified &lt; Restricted.
    /// A child can therefore tighten its parent but never relax it.
    /// </summary>
    private static RestrictionStatus EffectiveRestriction(Category category, Dictionary<int, Category> byId) =>
        AncestorChain(category, byId).Max(c => c.RestrictionStatus);

    /// <summary>
    /// Unclassified is handled conservatively — like Restricted — as a safety default. It is not a
    /// statement that the goods are restricted by law.
    /// </summary>
    private static bool RequiresAgeConfirmation(RestrictionStatus status) =>
        status is RestrictionStatus.Restricted or RestrictionStatus.Unclassified;

    private static List<CategoryPathDto> BuildPath(Category category, Dictionary<int, Category> byId) =>
        AncestorChain(category, byId)
            .Where(c => c.Id != category.Id)
            .Select(c => new CategoryPathDto(c.Id, c.Slug, c.NameAz))
            .ToList();

    private async Task<List<Category>> LoadAllAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            "categories:all",
            async token => await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(token),
            cancellationToken);

    private static List<CategoryNodeDto> BuildTree(List<Category> categories)
    {
        var byId = categories.ToDictionary(c => c.Id);

        return categories
            .Where(c => c.ParentId is null && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => ToNode(c, categories, byId))
            .ToList();
    }

    private static CategoryNodeDto ToNode(Category category, List<Category> all, Dictionary<int, Category> byId)
    {
        var effective = EffectiveRestriction(category, byId);

        var children = all
            .Where(c => c.ParentId == category.Id && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => ToNode(c, all, byId))
            .ToList();

        return new CategoryNodeDto(
            category.Id,
            category.Slug,
            category.NameAz,
            category.NameRu,
            category.IconKey,
            category.ImageKey,
            category.SortOrder,
            category.ListingCount,
            effective.ToString(),
            RequiresAgeConfirmation(effective),
            category.IsSelectable,
            children);
    }
}
