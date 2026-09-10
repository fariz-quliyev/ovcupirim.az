namespace Ovcuprim.Domain.Entities;

public class Favorite
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public Guid ListingId { get; set; }

    public Listing Listing { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
