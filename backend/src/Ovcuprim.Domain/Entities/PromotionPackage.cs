using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>
/// A purchasable promotion offering — admin-managed reference data, the same shape
/// <see cref="Category"/> already is. No row is seeded by this phase: names, durations, effects and
/// prices are a business decision, not something this codebase invents (see
/// docs/payment-integration-design.md, open decision #2). The table exists so the catalog can be
/// populated and edited through the admin API once that decision is made.
/// </summary>
public class PromotionPackage : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>Stable slug for internal/API reference — not a marketing name.</summary>
    public string Code { get; set; } = null!;

    public string NameAz { get; set; } = null!;

    public string? DescriptionAz { get; set; }

    public PromotionType Type { get; set; }

    public int DurationDays { get; set; }

    public decimal PriceAzn { get; set; }

    public string Currency { get; set; } = "AZN";

    /// <summary>False hides the package from the public catalog without deleting it.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public ICollection<Promotion> Promotions { get; set; } = [];
}
