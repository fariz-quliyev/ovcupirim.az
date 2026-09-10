namespace Ovcuprim.Application.Stores;

/// <summary>A seller's application for a storefront. The slug is derived, never supplied.</summary>
public sealed record ApplyForStoreRequest(
    string Name,
    string? Description,
    string? Address,
    string? Phone);

/// <summary>
/// Everything an owner may change afterwards. The name is editable; the slug it produced is not,
/// so a rename never moves the storefront's URL.
/// </summary>
public sealed record UpdateStoreRequest(
    string Name,
    string? Description,
    string? Address,
    string? Phone);

public sealed record RejectStoreRequest(string Reason);

public sealed record SuspendStoreRequest(string Reason);

/// <summary>The owner's own view: visible in every status, including one awaiting approval.</summary>
public sealed record StoreOwnerDto(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    string? Address,
    string? Phone,
    string Status,
    bool IsVerified,
    bool IsPublic,
    string? LogoUrl,
    string? BannerUrl,
    int ListingCount,
    int FollowerCount,
    DateTimeOffset CreatedAt);

/// <summary>
/// The public storefront. Carries the caller's own <see cref="IsFollowing"/>, which is why the
/// endpoint serving it must not be publicly cacheable.
/// </summary>
public sealed record StorePublicDto(
    string Slug,
    string Name,
    string? Description,
    string? Address,
    bool IsVerified,
    string? LogoUrl,
    string? BannerUrl,
    int ListingCount,
    int FollowerCount,
    bool ShowPhone,
    string? PhoneMasked,
    bool IsFollowing,
    DateTimeOffset MemberSince);

/// <summary>A row in the directory. No per-visitor state, so this one stays shareable.</summary>
public sealed record StoreCardDto(
    string Slug,
    string Name,
    string? LogoUrl,
    bool IsVerified,
    int ListingCount,
    int FollowerCount,
    DateTimeOffset MemberSince);

/// <summary>The moderator's queue view of an application or an active storefront.</summary>
public sealed record StoreAdminDto(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    string? Address,
    string? Phone,
    string Status,
    bool IsVerified,
    string OwnerName,
    Guid OwnerUserId,
    int ListingCount,
    DateTimeOffset CreatedAt);

public sealed record StorePhoneDto(string Phone);
