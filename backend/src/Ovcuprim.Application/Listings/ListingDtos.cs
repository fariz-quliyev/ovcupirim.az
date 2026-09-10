using System.Text.Json;
using Ovcuprim.Application.Categories;

namespace Ovcuprim.Application.Listings;

/// <summary>
/// Creates a draft. The category is fixed here and never changes afterwards: the whole attribute
/// schema hangs off it, so a different category means a different listing.
/// </summary>
public sealed record CreateListingRequest(
    string CategorySlug,
    string RegionSlug,
    string Title,
    string Description,
    decimal? Price,
    string Condition,
    string? Brand,
    bool HasDelivery,
    string ContactPhone,
    bool ShowPhone,
    Dictionary<string, JsonElement>? Attributes,
    /// <summary>
    /// Files the listing under the seller's own storefront. Ignored unless the caller owns an
    /// active store; SellerType is derived from the outcome and is never accepted from a client.
    /// </summary>
    bool UseStore = false);

/// <summary>
/// Replaces the editable fields. Which of them may actually change depends on the listing's
/// status — a published listing only accepts description, price, delivery and phone visibility
/// (see <see cref="ListingStateMachine.EditableFields"/>), and the server rejects the rest.
/// </summary>
public sealed record UpdateListingRequest(
    string RegionSlug,
    string Title,
    string Description,
    decimal? Price,
    string Condition,
    string? Brand,
    bool HasDelivery,
    string ContactPhone,
    bool ShowPhone,
    Dictionary<string, JsonElement>? Attributes);

/// <summary>Submits a draft, a rejected listing or a restored one for moderation.</summary>
public sealed record PublishListingRequest(bool AgeConfirmed);

public sealed record RejectListingRequest(string Reason);

public sealed record BlockListingRequest(string Reason);

public sealed record ReorderMediaRequest(IReadOnlyList<Guid> MediaIds);

public sealed record ListingMediaDto(
    Guid Id,
    string Url,
    IReadOnlyDictionary<string, string> Variants,
    int Width,
    int Height,
    long SizeBytes,
    int SortOrder,
    bool IsPrimary);

/// <summary>
/// A resolved attribute row for display. The value is formatted server-side against the category
/// schema so no category knowledge ever reaches a React component.
/// </summary>
public sealed record ListingAttributeDto(string Key, string LabelAz, string DisplayValue);

/// <summary>
/// The storefront a listing belongs to, as shown to the public. Present only while that storefront
/// is active: a suspended store offers no navigation, so the block is simply absent and the listing
/// presents as any individual seller's would.
/// </summary>
public sealed record ListingStoreDto(string Slug, string Name, bool IsVerified, string? LogoUrl);

/// <summary>
/// What the current actor may do next. Derived from <see cref="ListingStateMachine"/> so the
/// seller UI renders Tap.az-style per-status actions without restating the rules.
/// </summary>
public sealed record ListingCapabilitiesDto(
    bool Edit,
    bool Publish,
    bool Delete,
    bool Restore,
    bool MarkSold);

public sealed record ListingDetailDto(
    Guid Id,
    long ShortId,
    string Slug,
    string Status,
    string? RejectionReason,
    int CategoryId,
    string CategorySlug,
    string CategoryNameAz,
    IReadOnlyList<CategoryPathDto> CategoryPath,
    int RegionId,
    string RegionSlug,
    string RegionNameAz,
    string Title,
    string Description,
    decimal? Price,
    string Currency,
    string Condition,
    bool HasDelivery,
    string? Brand,
    string SellerType,
    string ContactPhone,
    bool ShowPhone,
    IReadOnlyDictionary<string, JsonElement> Attributes,
    IReadOnlyList<ListingAttributeDto> DisplayAttributes,
    IReadOnlyList<ListingMediaDto> Media,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RestorableUntil,
    int ViewCount,
    bool AgeConfirmed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    ListingCapabilitiesDto Can);

/// <summary>
/// The promotion currently running on the seller's own listing — what "İrəli çəkilib" on
/// "Mənim elanlarım" is built from (integration audit M-4). Absent when nothing is active.
/// </summary>
public sealed record ListingPromotionDto(
    string Status,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? ExpiresAt,
    int BumpIntervalHours);

/// <summary>Card shape for "Mənim elanlarım" and, later, for search results.</summary>
public sealed record ListingSummaryDto(
    Guid Id,
    long ShortId,
    string Slug,
    string Status,
    string? RejectionReason,
    string Title,
    decimal? Price,
    string Currency,
    string RegionNameAz,
    string CategoryNameAz,
    string? PrimaryImageUrl,
    int MediaCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RestorableUntil,
    int ViewCount,
    DateTimeOffset CreatedAt,
    ListingCapabilitiesDto Can,
    ListingPromotionDto? Promotion);

/// <summary>Public listing page. Never exposes the raw phone number; see the reveal endpoint.</summary>
public sealed record ListingPublicDto(
    long ShortId,
    string Slug,
    string CanonicalPath,
    string Title,
    string Description,
    decimal? Price,
    string Currency,
    string Condition,
    bool HasDelivery,
    string? Brand,
    string SellerType,
    int CategoryId,
    string CategorySlug,
    string CategoryNameAz,
    IReadOnlyList<CategoryPathDto> CategoryPath,
    string RegionSlug,
    string RegionNameAz,
    IReadOnlyList<ListingAttributeDto> Attributes,
    IReadOnlyList<ListingMediaDto> Media,
    /// <summary>
    /// The seller's own name, and null when <see cref="Store"/> is set: a listing presented as a
    /// storefront's does not disclose which person owns the shop. Populated for an individual sale
    /// and for the fallback when a storefront is no longer active, which is what the page shows
    /// instead of a store block.
    /// </summary>
    string? SellerName,
    /// <summary>Present only when the listing belongs to a storefront that is currently active.</summary>
    ListingStoreDto? Store,
    bool ShowPhone,
    string? ContactPhoneMasked,
    bool IsFavorited,
    DateTimeOffset PublishedAt,
    int ViewCount);

public sealed record ListingPhoneDto(string ContactPhone);

/// <summary>One category's free-listing budget, mirroring Tap.az's "Elan limiti" screen.</summary>
public sealed record ListingLimitDto(
    int CategoryId,
    string CategorySlug,
    string CategoryNameAz,
    int Used,
    int? Limit,
    DateTimeOffset? NextFreeSlotAt);

/// <summary>An entry in the moderation queue.</summary>
public sealed record ModerationQueueItemDto(
    Guid Id,
    long ShortId,
    string Title,
    string CategoryNameAz,
    string RestrictionStatus,
    bool IsStrict,
    string SellerName,
    IReadOnlyList<string> ScreeningFlags,
    DateTimeOffset SubmittedAt);
