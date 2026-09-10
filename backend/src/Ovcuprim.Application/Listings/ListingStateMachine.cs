using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings;

/// <summary>Fields a seller may change, which narrows once a listing has been published.</summary>
[Flags]
public enum ListingEditableFields
{
    None = 0,
    Description = 1,
    Price = 2,
    Delivery = 4,
    PhoneVisibility = 8,
    Media = 16,
    Title = 32,
    Region = 64,
    Condition = 128,
    Brand = 256,
    ContactPhone = 512,
    Attributes = 1024,

    /// <summary>Everything: a listing that has never been on the site yet.</summary>
    All = Description | Price | Delivery | PhoneVisibility | Media | Title | Region
        | Condition | Brand | ContactPhone | Attributes,

    /// <summary>
    /// What Tap.az allows after publication — image, description, price and delivery. Title and
    /// category stay frozen so a live listing cannot be swapped for a different product.
    /// </summary>
    Published = Description | Price | Delivery | PhoneVisibility | Media
}

/// <summary>
/// The single place that answers "what may happen to this listing next". Both the services and
/// the capability flags the seller UI renders read from here, so the rules are stated once.
/// </summary>
public static class ListingStateMachine
{
    /// <summary>How long an approved listing stays on the site.</summary>
    public static readonly TimeSpan ActiveLifetime = TimeSpan.FromDays(30);

    /// <summary>How long after expiry the seller can still bring a listing back.</summary>
    public static readonly TimeSpan RestoreWindow = TimeSpan.FromDays(30);

    /// <summary>Statuses whose content the seller can still change.</summary>
    public static bool CanEdit(ListingStatus status) => status is
        ListingStatus.Draft or ListingStatus.PendingModeration or ListingStatus.Rejected or ListingStatus.Active;

    public static ListingEditableFields EditableFields(ListingStatus status) => status switch
    {
        ListingStatus.Draft or ListingStatus.Rejected or ListingStatus.PendingModeration => ListingEditableFields.All,
        ListingStatus.Active => ListingEditableFields.Published,
        _ => ListingEditableFields.None
    };

    /// <summary>
    /// A published listing that changes in a way buyers can see goes back through moderation;
    /// hiding or showing the phone number does not.
    /// </summary>
    public static bool RequiresRemoderation(ListingStatus status, ListingEditableFields changed) =>
        status == ListingStatus.Active
        && (changed & ~ListingEditableFields.PhoneVisibility) != ListingEditableFields.None;

    public static bool CanPublish(ListingStatus status) => status is
        ListingStatus.Draft or ListingStatus.Rejected;

    /// <summary>A draft has never been public, so removing it deletes it outright.</summary>
    public static bool IsHardDeletable(ListingStatus status) => status is ListingStatus.Draft;

    /// <summary>Everything that has been submitted retires into <see cref="ListingStatus.Expired"/> instead.</summary>
    public static bool CanRetire(ListingStatus status) => status is
        ListingStatus.PendingModeration or ListingStatus.Active or ListingStatus.Rejected;

    public static bool CanDelete(ListingStatus status) => IsHardDeletable(status) || CanRetire(status);

    public static bool CanMarkSold(ListingStatus status) => status is
        ListingStatus.Active or ListingStatus.Expired;

    public static bool CanRestore(ListingStatus status, DateTimeOffset? expiresAt, DateTimeOffset now) =>
        status == ListingStatus.Expired && RestorableUntil(status, expiresAt) is { } until && now <= until;

    /// <summary>The deadline shown on an expired card; null when restoring does not apply.</summary>
    public static DateTimeOffset? RestorableUntil(ListingStatus status, DateTimeOffset? expiresAt) =>
        status == ListingStatus.Expired && expiresAt is { } expiry ? expiry + RestoreWindow : null;

    public static bool CanModerate(ListingStatus status) => status is ListingStatus.PendingModeration;

    public static bool CanBlock(ListingStatus status) => status is
        ListingStatus.Active or ListingStatus.PendingModeration;

    public static bool IsPubliclyVisible(ListingStatus status) => status is ListingStatus.Active;

    public static ListingCapabilitiesDto Capabilities(ListingStatus status, DateTimeOffset? expiresAt, DateTimeOffset now) =>
        new(
            Edit: CanEdit(status),
            Publish: CanPublish(status),
            Delete: CanDelete(status),
            Restore: CanRestore(status, expiresAt, now),
            MarkSold: CanMarkSold(status));
}
