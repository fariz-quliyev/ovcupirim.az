using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Infrastructure.Persistence.Seed;

internal sealed class CategorySeed
{
    public string Slug { get; set; } = null!;
    public string NameAz { get; set; } = null!;
    public string? NameRu { get; set; }
    public string? IconKey { get; set; }
    public string? DescriptionAz { get; set; }
    public string? DescriptionRu { get; set; }
    public int SortOrder { get; set; }
    public RestrictionStatus RestrictionStatus { get; set; } = RestrictionStatus.Unclassified;
    public List<CategorySeed> Children { get; set; } = [];
}

internal sealed class AttributeSeedFile
{
    public Dictionary<string, List<OptionSeed>> OptionSets { get; set; } = [];
    public List<AttributeSeed> Attributes { get; set; } = [];
}

internal sealed class AttributeSeed
{
    /// <summary>Slug of the category the definition is attached to.</summary>
    public string Category { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string LabelAz { get; set; } = null!;
    public string? LabelRu { get; set; }
    public AttributeDataType DataType { get; set; }
    public string? Unit { get; set; }
    public string? PlaceholderAz { get; set; }
    public string? PlaceholderRu { get; set; }
    public string? HelpTextAz { get; set; }
    public string? HelpTextRu { get; set; }
    public bool IsRequired { get; set; }
    public bool IsFilterable { get; set; } = true;
    public bool IsSearchable { get; set; }
    public bool AppliesToDescendants { get; set; } = true;
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public int? DecimalPlaces { get; set; }
    public int? MaxLength { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Name of a shared option set, so identical sets cannot drift apart.</summary>
    public string? OptionSet { get; set; }
}

internal sealed class OptionSeed
{
    public string Value { get; set; } = null!;
    public string LabelAz { get; set; } = null!;
    public string? LabelRu { get; set; }
}

internal sealed class StaticPageSeed
{
    public string Slug { get; set; } = null!;
    public StaticPageType PageType { get; set; } = StaticPageType.Info;
    public string TitleAz { get; set; } = null!;
    public string? TitleRu { get; set; }
    public string BodyAz { get; set; } = null!;
    public string? BodyRu { get; set; }
    public string? MetaDescriptionAz { get; set; }
    public string? ExcerptAz { get; set; }
    public bool IsPublished { get; set; }
    public int SortOrder { get; set; }
}

internal sealed class FaqCategorySeed
{
    public string Slug { get; set; } = null!;
    public string NameAz { get; set; } = null!;
    public string? NameRu { get; set; }
    public int SortOrder { get; set; }
    public List<FaqItemSeed> Items { get; set; } = [];
}

internal sealed class FaqItemSeed
{
    public string QuestionAz { get; set; } = null!;
    public string AnswerAz { get; set; } = null!;
    public string? QuestionRu { get; set; }
    public string? AnswerRu { get; set; }
    public int SortOrder { get; set; }
}

internal sealed class RegionSeedFile
{
    public List<RegionSeed> Regions { get; set; } = [];
}

internal sealed class RegionSeed
{
    public string Slug { get; set; } = null!;
    public string NameAz { get; set; } = null!;
    public string? NameRu { get; set; }
    public RegionType Type { get; set; } = RegionType.Rayon;
    public int SortOrder { get; set; }

    /// <summary>Only ever populated from an authoritative dataset — never invented.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

/// <summary>What a seed run inserted. Every value is zero on a second run over the same database.</summary>
public sealed record SeedReport(
    int Categories,
    int AttributeDefinitions,
    int AttributeOptions,
    int Regions,
    int StaticPages,
    int FaqCategories,
    int FaqItems)
{
    public int Total => Categories + AttributeDefinitions + AttributeOptions
                        + Regions + StaticPages + FaqCategories + FaqItems;

    public static SeedReport Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}
