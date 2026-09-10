using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Auth;

namespace Ovcuprim.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 with a server-held pepper. Deterministic, so a refresh token can be found by hash,
/// while a stolen database alone yields neither OTP codes nor refresh tokens.
/// </summary>
public sealed class SecretHasher : ISecretHasher
{
    private readonly byte[] _key;

    public SecretHasher(IOptions<SecurityOptions> options)
    {
        var key = options.Value.HashingKey;

        // Startup validation should have caught this already; kept as the last line of defence for
        // a host that constructs the hasher without going through AddApplication.
        if (string.IsNullOrWhiteSpace(key) || key.Length < SecurityOptions.MinimumHashingKeyLength)
        {
            throw new InvalidOperationException(
                $"Auth:Security:HashingKey must be configured with at least {SecurityOptions.MinimumHashingKeyLength} characters. " +
                "Set it through an environment variable or user-secrets — never in source control.");
        }

        _key = Encoding.UTF8.GetBytes(key);
    }

    public string Hash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(mac);
    }

    public bool Verify(string value, string hash)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        // Fixed-time comparison: a timing side channel would leak the code digit by digit.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(value)),
            Encoding.UTF8.GetBytes(hash));
    }
}
