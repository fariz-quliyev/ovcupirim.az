using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>A one-time SMS code. Stored hashed, single-use, attempt-limited.</summary>
public class OtpCode : AuditableEntity
{
    public Guid Id { get; set; }

    public string PhoneNumber { get; set; } = null!;

    public string CodeHash { get; set; } = null!;

    public OtpPurpose Purpose { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }
}
