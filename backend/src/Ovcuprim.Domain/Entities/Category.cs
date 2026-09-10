using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>Two-level taxonomy: top-level categories and their subcategories.</summary>
public class Category : AuditableEntity
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public Category? Parent { get; set; }

    /// <summary>0 for a top-level category, 1 for a subcategory. Depth beyond 1 is rejected in validation.</summary>
    public int Depth { get; set; }

    public string NameAz { get; set; } = null!;

    public string? NameRu { get; set; }

    public string Slug { get; set; } = null!;

    /// <summary>Retained when a slug changes so a redirect can be issued later.</summary>
    public string? PreviousSlug { get; set; }

    public string? DescriptionAz { get; set; }

    public string? DescriptionRu { get; set; }

    public string? MetaTitleAz { get; set; }

    public string? MetaTitleRu { get; set; }

    public string? MetaDescriptionAz { get; set; }

    public string? MetaDescriptionRu { get; set; }

    /// <summary>Key into the frontend icon set; top-level categories only.</summary>
    public string? IconKey { get; set; }

    /// <summary>Storage key for the category tile image.</summary>
    public string? ImageKey { get; set; }

    public int SortOrder { get; set; }

    /// <summary>False hides the category everywhere.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>False keeps the category browsable but closed to new listings.</summary>
    public bool IsSelectable { get; set; } = true;

    /// <summary>
    /// Product-restriction state. Defaults to <see cref="RestrictionStatus.Unclassified"/> so a new
    /// category is never silently treated as cleared before a policy decision exists.
    /// </summary>
    public RestrictionStatus RestrictionStatus { get; set; } = RestrictionStatus.Unclassified;

    /// <summary>Denormalised count of active listings, refreshed by a scheduled job.</summary>
    public int ListingCount { get; set; }

    public ICollection<Category> Children { get; set; } = [];

    public ICollection<AttributeDefinition> Attributes { get; set; } = [];

    public ICollection<Listing> Listings { get; set; } = [];
}
