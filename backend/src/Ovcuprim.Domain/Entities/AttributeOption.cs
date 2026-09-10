namespace Ovcuprim.Domain.Entities;

/// <summary>
/// An allowed value for a Select or MultiSelect attribute. <see cref="Value"/> is slug-safe because
/// it travels in filter query strings; <see cref="LabelAz"/> carries the display text.
/// </summary>
public class AttributeOption
{
    public int Id { get; set; }

    public int AttributeDefinitionId { get; set; }

    public AttributeDefinition AttributeDefinition { get; set; } = null!;

    public string Value { get; set; } = null!;

    public string LabelAz { get; set; } = null!;

    public string? LabelRu { get; set; }

    /// <summary>False removes the option from new listings and filter facets, but existing listings keep it.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}
