namespace Ovcuprim.Domain.Entities;

/// <summary>
/// One in-app notification. Never edited after it is written — only <see cref="ReadAt"/> changes,
/// the same append-mostly shape <see cref="AuditLog"/> uses for the same reason: what a seller was
/// actually told should still be answerable after the fact.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>Machine-readable, stable across releases — "listing.approved", "listing.rejected", "listing.blocked".</summary>
    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Body { get; set; } = null!;

    /// <summary>What this is about, mirroring AuditLog's EntityType/EntityId — "Listing" today, open to whatever notifies next.</summary>
    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
