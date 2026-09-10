namespace Ovcuprim.Application.Categories;

public sealed record CategoryNodeDto(
    int Id,
    string Slug,
    string NameAz,
    string? NameRu,
    string? IconKey,
    string? ImageKey,
    int SortOrder,
    int ListingCount,
    string RestrictionStatus,
    bool RequiresAgeConfirmation,
    bool IsSelectable,
    IReadOnlyList<CategoryNodeDto> Children);

public sealed record CategoryPathDto(int Id, string Slug, string NameAz);

public sealed record CategoryDetailDto(
    int Id,
    string Slug,
    string NameAz,
    string? NameRu,
    string? DescriptionAz,
    string? MetaTitleAz,
    string? MetaDescriptionAz,
    string? IconKey,
    string? ImageKey,
    int ListingCount,
    string RestrictionStatus,
    bool RequiresAgeConfirmation,
    bool IsSelectable,
    IReadOnlyList<CategoryPathDto> Path,
    IReadOnlyList<CategoryNodeDto> Children);

public sealed record AttributeOptionDto(string Value, string LabelAz, string? LabelRu);

/// <summary>
/// One field in a category's effective schema. Phase 4 renders a control from
/// <see cref="DataType"/> alone; Phase 5 builds a filter from the same record.
/// </summary>
public sealed record AttributeSchemaDto(
    string Key,
    string LabelAz,
    string? LabelRu,
    string DataType,
    string? Unit,
    bool IsRequired,
    bool IsFilterable,
    bool IsSearchable,
    decimal? MinValue,
    decimal? MaxValue,
    int? DecimalPlaces,
    int? MaxLength,
    string? PlaceholderAz,
    string? HelpTextAz,
    bool Inherited,
    int SortOrder,
    IReadOnlyList<AttributeOptionDto>? Options);

/// <summary>The contract between Phase 3, Phase 4 (listing creation) and Phase 5 (filtering).</summary>
public sealed record CategorySchemaDto(
    CategorySchemaCategoryDto Category,
    IReadOnlyList<AttributeSchemaDto> Attributes);

public sealed record CategorySchemaCategoryDto(
    int Id,
    string Slug,
    string NameAz,
    IReadOnlyList<CategoryPathDto> Path,
    string RestrictionStatus,
    bool RequiresAgeConfirmation,
    bool IsSelectable,
    bool IsLeaf);
