using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>A buyer's complaint about a listing.</summary>
public class Report : AuditableEntity
{
    public Guid Id { get; set; }

    public Guid ListingId { get; set; }

    public Listing Listing { get; set; } = null!;

    /// <summary>Null when reported anonymously.</summary>
    public Guid? ReporterUserId { get; set; }

    public User? ReporterUser { get; set; }

    public ReportReason Reason { get; set; }

    public string? Comment { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Open;

    public Guid? ResolvedByUserId { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
}
