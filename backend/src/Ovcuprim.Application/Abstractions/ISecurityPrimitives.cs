using Ovcuprim.Domain.Entities;

namespace Ovcuprim.Application.Abstractions;

/// <summary>
/// Keyed one-way hash for values that must be looked up but never readable in the database —
/// OTP codes and refresh tokens. Deterministic so a lookup by hash is possible; peppered so a
/// database leak alone cannot reverse it.
/// </summary>
public interface ISecretHasher
{
    string Hash(string value);

    /// <summary>Constant-time comparison — never compare hashes with ==.</summary>
    bool Verify(string value, string hash);
}

/// <summary>Cryptographically secure generators. Never <c>Random</c>.</summary>
public interface ISecureTokenGenerator
{
    /// <summary>A numeric one-time code of the requested length, uniformly distributed.</summary>
    string GenerateNumericCode(int length);

    /// <summary>A URL-safe 256-bit refresh token.</summary>
    string GenerateRefreshToken();
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
}
