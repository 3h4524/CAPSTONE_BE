using System.Text.Json;
using System.Text.RegularExpressions;

namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>
/// The product and listing words <see cref="ArtworkPromptRules.SplitKeywords"/> takes out of a product's keywords, read
/// once from <c>Vocabulary/artwork-vocabulary.json</c>, a file embedded in this assembly and edited by hand.
/// </summary>
internal static class ArtworkVocabulary
{
    /// <summary>Several words that together name the product or where it goes ("wall art", "phone case").</summary>
    public static Regex ProductPhrases { get; }

    /// <summary>The product, its sale or its listing ("shirt", "gift").</summary>
    public static IReadOnlySet<string> ProductWords { get; }

    /// <summary>Words that only join listing words ("for her" in "gift for her").</summary>
    public static IReadOnlySet<string> FillerWords { get; }

    static ArtworkVocabulary()
    {
        using var own = Read("artwork-vocabulary.json");
        var root = own.RootElement;
        var phrases = Words(root, "productPhrases").Select(phrase => string.Join(@"[\s-]+", phrase.Split(' ').Select(Regex.Escape)));
        ProductPhrases = new Regex($@"\b(?:{string.Join('|', phrases)})s?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        ProductWords = Set(Words(root, "productWords"));
        FillerWords = Set(Words(root, "fillerWords"));
    }

    private static JsonDocument Read(string fileName)
    {
        var name = $"APCS.Vocabulary.{fileName}";
        using var stream = typeof(ArtworkVocabulary).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded vocabulary file {name} is missing.");
        return JsonDocument.Parse(stream);
    }

    private static IEnumerable<string> Words(JsonElement root, string list) =>
        root.GetProperty(list).GetProperty("words").EnumerateArray().Select(word => word.GetString()!);

    private static HashSet<string> Set(IEnumerable<string> words) => new(words, StringComparer.OrdinalIgnoreCase);
}
