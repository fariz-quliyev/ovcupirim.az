namespace Ovcuprim.Domain.Enums;

/// <summary>
/// Canonical JSON representation in <c>Listings.Attributes</c>:
/// Text and Select store a JSON string, Number a JSON number, Boolean a JSON boolean,
/// and MultiSelect a JSON array of option-value strings.
/// </summary>
public enum AttributeDataType
{
    Text = 0,
    Number = 1,
    Boolean = 2,

    /// <summary>Single choice from <see cref="Entities.AttributeOption"/>.</summary>
    Select = 3,

    /// <summary>Multiple choices, stored as a JSON array.</summary>
    MultiSelect = 4
}
