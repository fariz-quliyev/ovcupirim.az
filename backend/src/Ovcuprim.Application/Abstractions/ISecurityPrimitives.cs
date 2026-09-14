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

/// <summary>
/// Slow, salted one-way hash for passwords. Deliberately a different interface from
/// <see cref="ISecretHasher"/>, which is fast and deterministic because an OTP code and a refresh
/// token have to be found by their hash. A password is never looked up by hash, so it gets the
/// opposite treatment: a per-password salt and a work factor that makes offline guessing expensive.
/// Using the keyed hash here would leave every password in the database crackable at the rate of a
/// single HMAC.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);

    /// <summary>
    /// A well-formed hash that no password can match. Verifying against it costs the same as a real
    /// check, so the "no such account" path can be made to take as long as the "wrong password" one
    /// — otherwise response time alone tells an attacker which phone numbers exist.
    /// </summary>
    string DecoyHash { get; }
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
