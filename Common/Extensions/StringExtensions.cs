using System.Text.RegularExpressions;
using APCS.Common.Constants;

namespace APCS.Common.Extensions;

/// <summary>
/// Provides small string helpers for cross-layer use.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Returns true when the value is null, empty, or whitespace.
    /// </summary>
    /// <param name="value">The string value to inspect.</param>
    /// <returns>True when the string has no useful content.</returns>
    public static bool IsNullOrWhiteSpace(this string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Truncates a string to the requested maximum length.
    /// </summary>
    /// <param name="value">The value to truncate.</param>
    /// <param name="maxLength">The maximum allowed length.</param>
    /// <returns>The original value or a truncated value.</returns>
    public static string Truncate(this string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (maxLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Maximum length cannot be negative.");
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>
    /// Converts text to a URL-friendly slug.
    /// </summary>
    /// <param name="value">The text to convert.</param>
    /// <returns>A lowercase slug.</returns>
    public static string ToSlug(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, RegexPatterns.SlugUnsafeCharacters, string.Empty, RegexOptions.None, TimeSpan.FromMilliseconds(100));
        normalized = Regex.Replace(normalized, @"\s+", "-", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        normalized = Regex.Replace(normalized, "-{2,}", "-", RegexOptions.None, TimeSpan.FromMilliseconds(100));

        return normalized.Trim('-');
    }
}
