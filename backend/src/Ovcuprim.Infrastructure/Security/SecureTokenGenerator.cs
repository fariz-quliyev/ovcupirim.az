using System.Security.Cryptography;
using Ovcuprim.Application.Abstractions;

namespace Ovcuprim.Infrastructure.Security;

public sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    public string GenerateNumericCode(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 8);

        Span<char> digits = stackalloc char[length];

        for (var i = 0; i < length; i++)
        {
            // Rejection-sampling RNG — uniform, unlike modulo over a random int.
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }

        return new string(digits);
    }

    public string GenerateRefreshToken()
    {
        // 256 bits of entropy, URL-safe so it survives a cookie round trip untouched.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
