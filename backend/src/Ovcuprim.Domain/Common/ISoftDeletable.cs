namespace Ovcuprim.Domain.Common;

/// <summary>Marks an entity that is hidden rather than removed. A global query filter excludes deleted rows.</summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
}
