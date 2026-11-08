using System.Text.RegularExpressions;

namespace APCS.Application.Features.Shops.Common;

/// <summary>
/// Makes listing text acceptable to Etsy before it is sent, and says what changed. The rules are Etsy's own,
/// from the createDraftListing reference: Etsy rejects the whole listing when one character is out of place.
/// </summary>
public static partial class EtsyListingText
{
    public const int MaxTitleLength = 140;
    public const int MaxTags = 13;
    public const int MaxTagLength = 20;

    /// <summary>Etsy allows each of these only once in a title.</summary>
    private static readonly char[] OncePerTitle = ['%', ':', '&', '+'];

    [GeneratedRegex(@"[^\p{L}\p{Nd}\p{P}\p{Sm}\p{Zs}™©®]")]
    private static partial Regex TitleForbidden();

    [GeneratedRegex(@"[^\p{L}\p{Nd}\p{Zs}\-'™©®]")]
    private static partial Regex TagForbidden();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static (string Title, IReadOnlyList<string> Warnings) Title(string? value)
    {
        var warnings = new List<string>();
        var title = Spaces().Replace(TitleForbidden().Replace(value ?? string.Empty, " "), " ").Trim();
        if (title != Spaces().Replace((value ?? string.Empty).Trim(), " "))
            warnings.Add("Characters Etsy does not allow in titles were removed.");

        foreach (var symbol in OncePerTitle)
        {
            var first = title.IndexOf(symbol);
            if (first < 0 || title.IndexOf(symbol, first + 1) < 0) continue;
            title = title[..(first + 1)] + title[(first + 1)..].Replace(symbol.ToString(), string.Empty);
            warnings.Add($"Etsy allows \"{symbol}\" once in a title; the repeats were removed.");
        }
        title = Spaces().Replace(title, " ").Trim();

        if (title.Length > MaxTitleLength)
        {
            var cut = title[..MaxTitleLength];
            var lastSpace = cut.LastIndexOf(' ');
            title = (lastSpace > MaxTitleLength / 2 ? cut[..lastSpace] : cut).TrimEnd(' ', ',', '-', '|');
            warnings.Add($"The title was shortened to Etsy's {MaxTitleLength} characters.");
        }
        return (title, warnings);
    }

    public static (IReadOnlyList<string> Tags, IReadOnlyList<string> Warnings) Tags(IEnumerable<string>? values)
    {
        var warnings = new List<string>();
        var tags = new List<string>();
        var tooLong = new List<string>();
        foreach (var raw in values ?? [])
        {
            var tag = Spaces().Replace(TagForbidden().Replace(raw, " "), " ").Trim();
            if (tag.Length == 0) continue;
            if (tag.Length > MaxTagLength) { tooLong.Add(tag); continue; }
            if (tags.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase))) continue;
            tags.Add(tag);
        }
        if (tooLong.Count > 0)
            warnings.Add($"Left out tag(s) longer than {MaxTagLength} characters: {string.Join(", ", tooLong)}.");
        if (tags.Count > MaxTags)
        {
            warnings.Add($"Etsy allows {MaxTags} tags; only the first {MaxTags} are sent.");
            tags = tags.Take(MaxTags).ToList();
        }
        return (tags, warnings);
    }
}
