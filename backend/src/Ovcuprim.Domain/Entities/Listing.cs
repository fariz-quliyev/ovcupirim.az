using System.Text.Json;
using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>A single item for sale. One product per listing.</summary>
public class Listing : AuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>Short sequential id used in the public URL: /elan/{slug}-{shortId}.</summary>
    public long ShortId { get; set; }

    /// <summary>Transliterated title slug, regenerated when the title changes.</summary>
    public string Slug { get; set; } = null!;

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>Set when the listing is published under a storefront.</summary>
    public Guid? StoreId { get; set; }

    public Store? Store { get; set; }

    /// <summary>Always the subcategory (a leaf), never a top-level category.</summary>
    public int CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public int RegionId { get; set; }

    public Region Region { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Description { get; set; } = null!;

    /// <summary>
    /// The asking price in <see cref="Currency"/>. Zero means the item is given away ("Pulsuz");
    /// null means no fixed price and is shown as "Razılaşma ilə".
    /// </summary>
    public decimal? Price { get; set; }

    public string Currency { get; set; } = "AZN";

    public ListingCondition Condition { get; set; }

    public bool HasDelivery { get; set; }

    public string? Brand { get; set; }

    public SellerType SellerType { get; set; }

    public string ContactPhone { get; set; } = null!;

    /// <summary>The phone is revealed to buyers only with the seller's consent.</summary>
    public bool ShowPhone { get; set; } = true;

    public ListingStatus Status { get; set; } = ListingStatus.Draft;

    public string? RejectionReason { get; set; }

    /// <summary>
    /// Per-category attribute values, stored as JSONB and filtered through a GIN index. Values keep
    /// their canonical JSON type: string for Text and Select, number for Number, boolean for
    /// Boolean, and an array of strings for MultiSelect.
    /// </summary>
    public Dictionary<string, JsonElement> Attributes { get; set; } = [];

    /// <summary>Accent-folded title and brand (ə→e, ı→i, ş→s …) backing trigram fuzzy search.</summary>
    public string? SearchKey { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Drives the default ordering; equals PublishedAt until the listing is renewed or promoted.</summary>
    public DateTimeOffset? BumpedAt { get; set; }

    /// <summary>
    /// Read counters. Written by the maintenance pass with direct SQL that deliberately leaves
    /// <see cref="Version"/> alone, so a page view can never invalidate a seller's open edit.
    /// </summary>
    public int ViewCount { get; set; }

    public int FavoriteCount { get; set; }

    /// <summary>
    /// Optimistic concurrency token, incremented by the context whenever the listing's own content
    /// changes.
    /// </summary>
    /// <remarks>
    /// This replaced PostgreSQL's <c>xmin</c>. A tuple's <c>xmin</c> advances on <em>any</em> update,
    /// including the counter statements above, so a seller who opened an edit form and saved a
    /// minute later could be told their listing had changed underneath them when all that happened
    /// was somebody viewing it. An application-managed version moves only when the content moves —
    /// and, unlike a system column, it exists on every provider, so the conflict path is testable.
    /// </remarks>
    public long Version { get; set; }

    /// <summary>
    /// When the seller acknowledged the age requirement for a restricted or unclassified category.
    /// Null for ordinary listings; populated by the restricted-listing flow in Phase 4.
    /// </summary>
    public DateTimeOffset? AgeConfirmedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<ListingMedia> Media { get; set; } = [];

    public ICollection<Favorite> Favorites { get; set; } = [];

    public ICollection<Report> Reports { get; set; } = [];

    public ICollection<ModerationAction> ModerationActions { get; set; } = [];

    public ICollection<PaymentOrder> PaymentOrders { get; set; } = [];

    public ICollection<Promotion> Promotions { get; set; } = [];
}
