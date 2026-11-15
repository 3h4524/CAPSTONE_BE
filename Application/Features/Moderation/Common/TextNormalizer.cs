using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace APCS.Application.Features.Moderation.Common;

/// <summary>
/// Undoes the usual ways of writing round a blocklist before it is matched: case, look-alike digits and symbols
/// ("s3x", "b@by") and letters spaced out with punctuation ("s.e.x", "s e x"). Repeated letters ("nudeee") are left
/// alone: <see cref="BlocklistMatcher"/> lets every letter of a term repeat. Vietnamese accents are kept, because
/// removing them turns many everyday words into others; <see cref="RemoveAccents"/> is applied only where a term asks.
/// </summary>
public static partial class TextNormalizer
{
    private static readonly Dictionary<char, char> LookAlikes = new()
    {
        ['0'] = 'o', ['1'] = 'i', ['3'] = 'e', ['4'] = 'a', ['5'] = 's', ['7'] = 't', ['@'] = 'a', ['$'] = 's'
    };

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lower = text.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        // Only inside a word that has a letter, so sizes and years ("2048", "2026") stay numbers.
        lower = Token().Replace(lower, match => match.Value.Any(char.IsLetter) ? ReplaceLookAlikes(match.Value) : match.Value);
        return SpacedLetters().Replace(lower, match => new string(match.Value.Where(char.IsLetter).ToArray()));
    }

    /// <summary>The text without Vietnamese (or any combining) accents, "đ" read as "d".</summary>
    public static string RemoveAccents(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(character switch { 'đ' => 'd', 'Đ' => 'D', _ => character });
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string ReplaceLookAlikes(string token)
    {
        var characters = token.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
            if (LookAlikes.TryGetValue(characters[index], out var letter)) characters[index] = letter;
        return new string(characters);
    }

    [GeneratedRegex(@"\S+", RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    // Three or more single letters, each split from the next by one non-letter ("s.e.x", "s e x", "s-e-x").
    [GeneratedRegex(@"(?<![\p{L}\p{N}])\p{L}(?:[^\p{L}\p{N}\r\n]\p{L}){2,}(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex SpacedLetters();
}
