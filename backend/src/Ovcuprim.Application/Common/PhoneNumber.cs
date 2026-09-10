using System.Text.RegularExpressions;

namespace Ovcuprim.Application.Common;

/// <summary>
/// Azerbaijani mobile numbers, normalised to E.164 so the same person cannot register twice
/// as "050 123 45 67" and "+994501234567".
/// </summary>
public static partial class PhoneNumber
{
    private const string CountryCode = "994";

    [GeneratedRegex(@"^\+994(10|50|51|55|60|70|77|99)\d{7}$")]
    private static partial Regex E164Pattern();

    /// <summary>Strips punctuation and adds the country code. Returns null when the result is not a valid mobile number.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());

        digits = digits switch
        {
            // 0501234567 -> 994501234567
            { Length: 10 } when digits[0] == '0' => CountryCode + digits[1..],
            // 501234567 -> 994501234567
            { Length: 9 } => CountryCode + digits,
            // 00994501234567 -> 994501234567
            { Length: 14 } when digits.StartsWith("00" + CountryCode, StringComparison.Ordinal) => digits[2..],
            _ => digits
        };

        var candidate = "+" + digits;

        return E164Pattern().IsMatch(candidate) ? candidate : null;
    }

    public static bool IsValid(string? input) => Normalize(input) is not null;

    /// <summary>Masks all but the last two digits, for logs and for the "code sent" screen.</summary>
    public static string Mask(string e164)
    {
        if (e164.Length < 6)
        {
            return "***";
        }

        return string.Concat(e164.AsSpan(0, 7), "***", e164.AsSpan(e164.Length - 2));
    }
}
