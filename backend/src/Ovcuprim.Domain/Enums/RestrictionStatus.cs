namespace Ovcuprim.Domain.Enums;

/// <summary>
/// Whether a category carries a product restriction. The numeric values encode severity so the
/// effective status of a category is simply the MAX across its ancestor chain.
/// </summary>
/// <remarks>
/// <see cref="Unclassified"/> means no policy decision has been recorded — it is NOT a statement
/// that the goods are restricted by law. The runtime handles it conservatively (like
/// <see cref="Restricted"/>) as a safety default, but every user-facing surface must present it as
/// pending classification.
/// </remarks>
public enum RestrictionStatus
{
    Unrestricted = 0,
    Unclassified = 1,
    Restricted = 2
}
