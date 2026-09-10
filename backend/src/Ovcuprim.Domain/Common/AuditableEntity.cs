namespace Ovcuprim.Domain.Common;

/// <summary>Base for entities that carry creation and modification timestamps (always UTC).</summary>
public abstract class AuditableEntity
{
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
