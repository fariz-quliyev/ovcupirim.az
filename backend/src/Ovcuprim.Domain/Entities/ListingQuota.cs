namespace Ovcuprim.Domain.Entities;

/// <summary>Tracks a user's free listings per category for one calendar month.</summary>
public class ListingQuota
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public int CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    /// <summary>First day of the calendar month this quota covers.</summary>
    public DateOnly PeriodStart { get; set; }

    public int UsedCount { get; set; }
}
