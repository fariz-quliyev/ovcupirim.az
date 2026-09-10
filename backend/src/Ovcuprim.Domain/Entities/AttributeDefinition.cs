using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>
/// Defines one per-category attribute. Adding a filter is a content change, not a deployment.
/// A definition applies to the category it is attached to and, when
/// <see cref="AppliesToDescendants"/> is set, to every descendant that does not define the same
/// <see cref="Key"/> itself.
/// </summary>
public class AttributeDefinition : AuditableEntity
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    /// <summary>Key used inside the listing's JSONB attribute bag and in filter query strings.</summary>
    public string Key { get; set; } = null!;

    public string LabelAz { get; set; } = null!;

    public string? LabelRu { get; set; }

    public AttributeDataType DataType { get; set; } = AttributeDataType.Text;

    /// <summary>Display unit, e.g. "m", "kq", "L".</summary>
    public string? Unit { get; set; }

    public string? PlaceholderAz { get; set; }

    public string? PlaceholderRu { get; set; }

    public string? HelpTextAz { get; set; }

    public string? HelpTextRu { get; set; }

    public bool IsFilterable { get; set; } = true;

    /// <summary>Whether the value joins the listing's search document.</summary>
    public bool IsSearchable { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>False retires the attribute without deleting listings that already carry a value.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Inclusive lower bound for <see cref="AttributeDataType.Number"/>.</summary>
    public decimal? MinValue { get; set; }

    /// <summary>Inclusive upper bound for <see cref="AttributeDataType.Number"/>.</summary>
    public decimal? MaxValue { get; set; }

    /// <summary>Digits kept after the decimal point when canonicalising a number.</summary>
    public int? DecimalPlaces { get; set; }

    /// <summary>Maximum length for <see cref="AttributeDataType.Text"/>.</summary>
    public int? MaxLength { get; set; }

    /// <summary>When true, descendants inherit this definition unless they override the key.</summary>
    public bool AppliesToDescendants { get; set; } = true;

    public int SortOrder { get; set; }

    public ICollection<AttributeOption> Options { get; set; } = [];
}
