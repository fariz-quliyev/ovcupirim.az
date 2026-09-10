using Ovcuprim.Domain.Common;

namespace Ovcuprim.Domain.Entities;

/// <summary>An advertiser's enquiry submitted from the "Reklam yerləşdirin" page.</summary>
public class AdInquiry : AuditableEntity
{
    public Guid Id { get; set; }

    public string Company { get; set; } = null!;

    public string Contact { get; set; } = null!;

    public string? Note { get; set; }

    public bool IsHandled { get; set; }
}
