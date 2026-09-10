namespace Ovcuprim.Domain.Enums;

/// <summary>Lifecycle of a listing. Every new or edited listing re-enters <see cref="PendingModeration"/>.</summary>
public enum ListingStatus
{
    Draft = 0,
    PendingModeration = 1,
    Active = 2,
    Rejected = 3,
    Expired = 4,
    Archived = 5,
    Sold = 6,
    Blocked = 7
}
