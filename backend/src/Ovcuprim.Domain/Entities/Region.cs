using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>
/// Azerbaijani administrative unit. Two levels at most: cities and rayons at the top, city
/// districts beneath. Coordinates drive the map discovery view and are populated from the
/// authoritative dataset, never invented.
/// </summary>
public class Region : AuditableEntity
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public Region? Parent { get; set; }

    public int Depth { get; set; }

    public RegionType Type { get; set; } = RegionType.Rayon;

    public string NameAz { get; set; } = null!;

    public string? NameRu { get; set; }

    public string Slug { get; set; } = null!;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether users may pick this as a listing location. The administrative hierarchy is kept for
    /// data integrity, but only practical places — cities and rayons — are offered in the picker.
    /// Internal records such as city raions and settlements stay stored with this set to false.
    /// </summary>
    public bool IsSelectable { get; set; } = true;

    /// <summary>Denormalised count of active listings, refreshed by a scheduled job.</summary>
    public int ListingCount { get; set; }

    public int SortOrder { get; set; }

    public ICollection<Region> Children { get; set; } = [];

    public ICollection<Listing> Listings { get; set; } = [];
}
