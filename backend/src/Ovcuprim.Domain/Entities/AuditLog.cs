namespace Ovcuprim.Domain.Entities;

/// <summary>Append-only trail of administrative actions.</summary>
public class AuditLog
{
    public Guid Id { get; set; }

    public Guid? ActorUserId { get; set; }

    public string EntityType { get; set; } = null!;

    public string EntityId { get; set; } = null!;

    public string Action { get; set; } = null!;

    /// <summary>Serialised change payload. Never contains credentials.</summary>
    public string? PayloadJson { get; set; }

    public string? IpAddress { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
