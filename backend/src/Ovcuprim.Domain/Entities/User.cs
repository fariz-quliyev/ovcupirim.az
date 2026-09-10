using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

public class User : AuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>E.164 without spaces, e.g. +994501234567. The account's unique identity.</summary>
    public string PhoneNumber { get; set; } = null!;

    public bool IsPhoneVerified { get; set; }

    /// <summary>Optional — used for recovery and notifications only.</summary>
    public string? Email { get; set; }

    /// <summary>Null when the account authenticates by OTP alone.</summary>
    public string? PasswordHash { get; set; }

    public string FullName { get; set; } = null!;

    public UserRole Role { get; set; } = UserRole.User;

    public UserStatus Status { get; set; } = UserStatus.Active;

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Store? Store { get; set; }

    public ICollection<Listing> Listings { get; set; } = [];

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    public ICollection<Favorite> Favorites { get; set; } = [];
}
