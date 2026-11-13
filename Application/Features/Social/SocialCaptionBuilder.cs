using System.Text;
using System.Text.RegularExpressions;

namespace APCS.Application.Features.Social;

/// <summary>The words of a product's listing, as the share kit reads them.</summary>
public sealed record SocialListingText(string Title, string Description, IReadOnlyList<string> Tags, string? ListingUrl);

/// <summary>A Pinterest pin's text: what Pinterest shows, within its limits.</summary>
public sealed record PinterestText(string Title, string Description, string AltText);

/// <summary>
/// Writes the text of a product's social posts from its Etsy listing: titles cut at a phrase within each platform's
/// limit, a short description with the listing's keywords, and hashtags made from its tags. Nothing is invented: every
/// word comes from the listing the seller already approved.
/// </summary>
public static partial class SocialCaptionBuilder
{
    public const int PinterestTitleMax = 100;
    public const int PinterestDescriptionMax = 500;
    public const int PinterestAltTextMax = 500;
    public const int TikTokCaptionMax = 2200;

    /// <summary>Hashtags on a TikTok post: a few relevant ones read better than many.</summary>
    public const int TikTokHashtags = 5;

    /// <summary>Keyword phrases listed under a pin's description: Pinterest searches the description.</summary>
    public const int PinterestKeywords = 5;

    /// <summary>Added to TikTok hashtags when the listing's own tags give fewer.</summary>
    private static readonly string[] ShopHashtags = ["#etsyfinds", "#smallbusiness", "#shopsmall"];

    public static PinterestText Pinterest(SocialListingText listing, string productName)
    {
        var title = CutAtPhrase(listing.Title, PinterestTitleMax);
        var keywords = string.Join(" · ", listing.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Take(PinterestKeywords).Select(tag => tag.Trim()));
        var closing = listing.ListingUrl is null ? null : "Shop it on Etsy.";
        var room = PinterestDescriptionMax - (keywords.Length == 0 ? 0 : keywords.Length + 2) - (closing is null ? 0 : closing.Length + 1);
        var body = Summary(listing.Description, Math.Max(80, room));
        var description = new StringBuilder(body);
        if (closing is not null) description.Append(' ').Append(closing);
        if (keywords.Length > 0) description.Append("\n\n").Append(keywords);
        var alt = Cut($"{CutAtPhrase(listing.Title, 200)}, shown on a mock-up of the product ({productName}).", PinterestAltTextMax);
        return new PinterestText(title, Cut(description.ToString(), PinterestDescriptionMax), alt);
    }

    /// <summary>A TikTok caption: the product in a line, where to buy it, and hashtags from the listing's tags.</summary>
    public static string TikTok(SocialListingText listing)
    {
        var hook = CutAtPhrase(FirstPhrase(listing.Title), 120);
        var lines = new List<string> { $"{hook} ✨" };
        if (listing.ListingUrl is not null)
            lines.Add("Find it in our Etsy shop — link in bio.");
        lines.Add(string.Join(' ', Hashtags(listing.Tags, TikTokHashtags)));
        return Cut(string.Join("\n\n", lines), TikTokCaptionMax);
    }

    /// <summary>Hashtags from the listing's tags ("math pun shirt" → #mathpunshirt), then shop ones to reach <paramref name="count"/>.</summary>
    public static IReadOnlyList<string> Hashtags(IEnumerable<string> tags, int count)
    {
        var result = new List<string>();
        foreach (var tag in tags)
        {
            var word = NotHashtag().Replace(tag.ToLowerInvariant(), string.Empty);
            if (word.Length is < 3 or > 30 || result.Contains($"#{word}")) continue;
            result.Add($"#{word}");
            if (result.Count == count) return result;
        }
        foreach (var shop in ShopHashtags)
        {
            if (result.Count == count) break;
            if (!result.Contains(shop)) result.Add(shop);
        }
        return result;
    }

    /// <summary>The first phrase of an Etsy title ("Funny Math Teacher Shirt, Pi Day Gift, …" → "Funny Math Teacher Shirt").</summary>
    private static string FirstPhrase(string title)
    {
        var first = title.Split([',', '|', '–', '—'], 2)[0].Trim();
        return first.Length >= 12 ? first : title.Trim();
    }

    /// <summary>The longest run of whole phrases (split at commas and bars) within <paramref name="max"/>; else whole words.</summary>
    private static string CutAtPhrase(string text, int max)
    {
        text = Spaces().Replace(text.Trim(), " ");
        if (text.Length <= max) return text;
        var phrases = text.Split([',', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var kept = new StringBuilder();
        foreach (var phrase in phrases)
        {
            var next = kept.Length == 0 ? phrase : $"{kept}, {phrase}";
            if (next.Length > max) break;
            kept.Clear().Append(next);
        }
        return kept.Length > 0 ? kept.ToString() : Cut(text, max);
    }

    /// <summary>The description's first sentences within <paramref name="max"/>, without blank lines or bullet clutter.</summary>
    private static string Summary(string description, int max)
    {
        var flat = Spaces().Replace(string.Join(' ', description.Split('\n')
            .Select(line => line.Trim().TrimStart('•', '-', '*', '✔', '✅', '▸', '.', ':').Trim())
            .Where(line => line.Length > 0)), " ");
        if (flat.Length <= max) return flat;
        var sentences = Sentence().Split(flat);
        var kept = new StringBuilder();
        foreach (var sentence in sentences)
        {
            if (kept.Length + sentence.Length + 1 > max) break;
            if (kept.Length > 0) kept.Append(' ');
            kept.Append(sentence);
        }
        return kept.Length > 0 ? kept.ToString() : Cut(flat, max);
    }

    /// <summary>At most <paramref name="max"/> characters, cut at a word with an ellipsis.</summary>
    private static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        var cut = text[..(max - 1)];
        var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut).TrimEnd(',', ' ', '.') + "…";
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotHashtag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex Sentence();
}
