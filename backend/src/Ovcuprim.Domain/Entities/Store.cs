using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>
/// A seller storefront. One per user, enforced by a unique index on <see cref="OwnerUserId"/>.
/// </summary>
/// <remarks>
/// A storefront is public only while it is <see cref="StoreStatus.Active"/> and its owner still
/// exists. Suspending one hides the shopfront, not the goods: the owner's listings stay live and
/// simply stop offering any navigation back to the store.
/// </remarks>
public class Store : AuditableEntity
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    public User OwnerUser { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>
    /// Storage keys for the storefront's own images. Plain keys rather than ListingMedia rows,
    /// because that table is listing-scoped; the upload pipeline (magic-byte check, decode bounds,
    /// EXIF stripping, re-encode, random key) is the same one listings use.
    /// </summary>
    public string? LogoStorageKey { get; set; }

    public string? BannerStorageKey { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    /// <summary>Verified badge — granted by an administrator, not self-service.</summary>
    public bool IsVerified { get; set; }

    public StoreStatus Status { get; set; } = StoreStatus.PendingVerification;

    /// <summary>Live listings filed under this store. Recomputed by the maintenance pass.</summary>
    public int ListingCount { get; set; }

    public int FollowerCount { get; set; }

    /// <summary>
    /// Optimistic concurrency token, incremented by the context on a content change and left alone
    /// by the counter statements above — the same discipline the listing token follows.
    /// </summary>
    public long Version { get; set; }

    public ICollection<Listing> Listings { get; set; } = [];

    public ICollection<StoreFollow> Followers { get; set; } = [];
}
