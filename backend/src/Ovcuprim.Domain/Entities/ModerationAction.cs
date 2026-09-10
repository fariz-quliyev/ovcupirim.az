using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>Append-only record of a moderator's decision on a listing.</summary>
public class ModerationAction
{
    public Guid Id { get; set; }

    public Guid ListingId { get; set; }

    public Listing Listing { get; set; } = null!;

    public Guid ModeratorUserId { get; set; }

    public User ModeratorUser { get; set; } = null!;

    public ModerationActionType Action { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
