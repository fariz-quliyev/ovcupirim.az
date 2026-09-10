using System.Text;

namespace Ovcuprim.Application.Common;

/// <summary>
/// Folds Azerbaijani letters to ASCII and builds URL slugs. One implementation is shared by
/// category slugs, region slugs, attribute option values and the listing search key, so a term
/// normalises identically everywhere.
/// </summary>
public static class AzerbaijaniText
{
    /// <summary>
    /// Case is handled explicitly per character rather than by <c>ToLower</c>, because Azerbaijani
    /// has two i-pairs: I↔ı (dotless) and İ↔i (dotted). An invariant lower-casing maps 'I' to 'i',
    /// which is wrong for Azerbaijani; both pairs fold to plain 'i' for slug and search purposes.
    /// </summary>
    private static string? Fold(char c) => c switch
    {
        'ə' or 'Ə' => "e",
        'ı' or 'I' => "i",
        'i' or 'İ' => "i",
        'ş' or 'Ş' => "s",
        'ç' or 'Ç' => "c",
        'ğ' or 'Ğ' => "g",
        'ö' or 'Ö' => "o",
        'ü' or 'Ü' => "u",
        _ => null
    };

    /// <summary>
    /// Accent-folded, lower-cased form used for search matching. Preserves spacing and digits;
    /// characters outside the Azerbaijani and ASCII ranges are dropped.
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);

        foreach (var c in input)
        {
            var folded = Fold(c);

            if (folded is not null)
            {
                builder.Append(folded);
            }
            else if (char.IsAsciiLetter(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsAsciiDigit(c))
            {
                builder.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                builder.Append(' ');
            }
        }

        return CollapseWhitespace(builder.ToString()).Trim();
    }

    /// <summary>
    /// URL slug: folded, lower-case, hyphen-separated, ASCII only. "Çanta və aksesuar" becomes
    /// "canta-ve-aksesuar".
    /// </summary>
    public static string ToSlug(string? input, int maxLength = 80)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);

        foreach (var c in input)
        {
            var folded = Fold(c);

            if (folded is not null)
            {
                builder.Append(folded);
            }
            else if (char.IsAsciiLetter(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsAsciiDigit(c))
            {
                builder.Append(c);
            }
            else
            {
                // Everything else — spaces, punctuation, unknown scripts — becomes a separator.
                builder.Append('-');
            }
        }

        var slug = CollapseHyphens(builder.ToString()).Trim('-');

        if (slug.Length > maxLength)
        {
            slug = slug[..maxLength].TrimEnd('-');
        }

        return slug;
    }

    /// <summary>True when the value is already a valid slug: lower-case ASCII, digits and hyphens.</summary>
    public static bool IsSlug(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        char? previous = null;

        foreach (var c in value)
        {
            var valid = char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-';

            if (!valid || (c == '-' && previous == '-'))
            {
                return false;
            }

            previous = c;
        }

        return true;
    }

    private static string CollapseHyphens(string value) => Collapse(value, '-');

    private static string CollapseWhitespace(string value) => Collapse(value, ' ');

    private static string Collapse(string value, char separator)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var c in value)
        {
            if (c == separator)
            {
                if (!previousWasSeparator)
                {
                    builder.Append(c);
                }

                previousWasSeparator = true;
            }
            else
            {
                builder.Append(c);
                previousWasSeparator = false;
            }
        }

        return builder.ToString();
    }
}
