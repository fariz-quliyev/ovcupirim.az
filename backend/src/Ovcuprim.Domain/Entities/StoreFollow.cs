namespace Ovcuprim.Domain.Entities;

/// <summary>Join entity: a user follows a store and is notified of its new listings.</summary>
public class StoreFollow
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public Guid StoreId { get; set; }

    public Store Store { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
