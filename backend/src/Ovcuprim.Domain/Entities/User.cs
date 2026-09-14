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

    /// <summary>
    /// Null when the account authenticates by OTP alone, which is every ordinary user. Only an
    /// administrator has one: the admin panel is reached by password and never by SMS.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Consecutive failed password attempts. Reset by a successful sign-in and by a password change.
    /// </summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>
    /// Set when <see cref="FailedLoginAttempts"/> crosses the threshold. While it is in the future
    /// the password is refused no matter how correct it is, which is what makes guessing expensive
    /// for an attacker who has more addresses than the per-IP limiter can see.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }

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
