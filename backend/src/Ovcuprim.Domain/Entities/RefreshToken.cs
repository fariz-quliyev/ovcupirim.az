using Ovcuprim.Domain.Common;

namespace Ovcuprim.Domain.Entities;

/// <summary>A rotating refresh token. Only the hash is stored; the raw value lives in an HttpOnly cookie.</summary>
public class RefreshToken : AuditableEntity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
