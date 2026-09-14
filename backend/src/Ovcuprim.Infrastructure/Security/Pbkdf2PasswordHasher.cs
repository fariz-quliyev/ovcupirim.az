using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Auth;

namespace Ovcuprim.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA512 over a peppered password, with a per-password salt.
/// </summary>
/// <remarks>
/// Two layers, each covering a different theft:
/// <list type="bullet">
/// <item>The <b>salt</b> is random per password and stored alongside the hash. It defeats rainbow
/// tables and means two accounts with the same password do not share a hash.</item>
/// <item>The <b>pepper</b> is the server-held <c>Auth:Security:HashingKey</c>, HMAC'd over the
/// password before the derivation. It is not in the database, so a stolen database alone — a dump,
/// a backup, a leaked replica — yields nothing to attack offline.</item>
/// </list>
/// The iteration count is stored inside each hash rather than assumed, so raising it later only
/// affects passwords set from then on and old hashes keep verifying.
///
/// Rotating <c>HashingKey</c> invalidates every stored password, exactly as it already invalidates
/// every refresh token and outstanding OTP. That is the accepted cost of peppering; an operator
/// rotating the key must reset the administrator password with <c>--set-admin-password</c>.
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>OWASP's 2023 floor for PBKDF2-HMAC-SHA512. Raise it, never lower it.</summary>
    private const int DefaultIterations = 210_000;

    /// <summary>
    /// A tampered or corrupted row must not be able to turn one login attempt into a CPU exhaustion
    /// attack, so a stored iteration count above this is treated as a malformed hash.
    /// </summary>
    private const int MaximumIterations = 1_000_000;

    private const int SaltBytes = 16;
    private const int SubkeyBytes = 32;
    private const string Version = "v1";

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    private readonly byte[] _pepper;

    public Pbkdf2PasswordHasher(IOptions<SecurityOptions> options)
    {
        var key = options.Value.HashingKey;

        // Startup validation should have caught this already; kept as the last line of defence, and
        // identical to the check SecretHasher makes, because both read the same key.
        if (string.IsNullOrWhiteSpace(key) || key.Length < SecurityOptions.MinimumHashingKeyLength)
        {
            throw new InvalidOperationException(
                $"Auth:Security:HashingKey must be configured with at least {SecurityOptions.MinimumHashingKeyLength} characters. " +
                "Set it through an environment variable or user-secrets — never in source control.");
        }

        _pepper = Encoding.UTF8.GetBytes(key);
    }

    /// <summary>All-zero salt and subkey: well-formed, parses, costs a full derivation, matches nothing.</summary>
    public string DecoyHash { get; } = string.Create(
        CultureInfo.InvariantCulture,
        $"{Version}.{DefaultIterations}.{Convert.ToBase64String(new byte[SaltBytes])}.{Convert.ToBase64String(new byte[SubkeyBytes])}");

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var subkey = Derive(password, salt, DefaultIterations);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Version}.{DefaultIterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(subkey)}");
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        if (!TryParse(hash, out var salt, out var expected, out var iterations))
        {
            return false;
        }

        var actual = Derive(password, salt, iterations);

        // Fixed-time comparison: anything else leaks the expected hash byte by byte.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private byte[] Derive(string password, byte[] salt, int iterations)
    {
        // The pepper enters here, not as part of the salt: HMAC first, then stretch. A database
        // without the key cannot even begin the derivation.
        var peppered = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(password));

        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(peppered, salt, iterations, Algorithm, SubkeyBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(peppered);
        }
    }

    private static bool TryParse(string hash, out byte[] salt, out byte[] subkey, out int iterations)
    {
        salt = [];
        subkey = [];
        iterations = 0;

        var parts = hash.Split('.');

        if (parts.Length != 4 || parts[0] != Version)
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations < 1
            || iterations > MaximumIterations)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            subkey = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length == SaltBytes && subkey.Length == SubkeyBytes;
    }
}
