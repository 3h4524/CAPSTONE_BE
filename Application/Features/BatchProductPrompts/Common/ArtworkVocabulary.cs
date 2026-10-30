using System.Text.Json;
using System.Text.RegularExpressions;

namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>
/// The words <see cref="ArtworkPromptRules.SplitKeywords"/> sorts a product's keywords by, read once from two files
/// embedded in this assembly: <c>Vocabulary/artwork-vocabulary.json</c>, the app's own words (the product, lettering,
/// who it is for, the feel), edited by hand; and <c>Vocabulary/wordnet-words.json</c>, English word knowledge (every
/// word for a relative, every word used mostly as an adjective) built from WordNet by
/// <c>scripts/lexicon/build-wordnet-words.py</c>.
/// </summary>
internal static class ArtworkVocabulary
{
    /// <summary>Several words that together name the product or where it goes ("wall art", "phone case").</summary>
    public static Regex ProductPhrases { get; }

    /// <summary>The product, its sale or its listing ("shirt", "gift").</summary>
    public static IReadOnlySet<string> ProductWords { get; }

    /// <summary>Words that ask for lettering ("quote", "typography").</summary>
    public static IReadOnlySet<string> TextWords { get; }

    /// <summary>Who the product is for, other than a relative ("friend", "lover").</summary>
    public static IReadOnlySet<string> PeopleWords { get; }

    /// <summary>Words for the feel of a design, and the mood each becomes ("funny" is "playful").</summary>
    public static IReadOnlyDictionary<string, string> MoodWords { get; }

    /// <summary>Praise that names no feel a picture could show ("cool", "best").</summary>
    public static IReadOnlySet<string> EmptyWords { get; }

    /// <summary>Words that only join listing words ("for" in "gift for her").</summary>
    public static IReadOnlySet<string> FillerWords { get; }

    /// <summary>Every word for a relative, with plurals ("mom", "granddaughters"), from WordNet.</summary>
    public static IReadOnlySet<string> FamilyWords { get; }

    /// <summary>Every word used mostly as an adjective ("geometric", "coastal"), from WordNet.</summary>
    public static IReadOnlySet<string> Adjectives { get; }

    static ArtworkVocabulary()
    {
        using var own = Read("artwork-vocabulary.json");
        var root = own.RootElement;
        var phrases = Words(root, "productPhrases").Select(phrase => string.Join(@"[\s-]+", phrase.Split(' ').Select(Regex.Escape)));
        ProductPhrases = new Regex($@"\b(?:{string.Join('|', phrases)})s?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        ProductWords = Set(Words(root, "productWords"));
        TextWords = Set(Words(root, "textWords"));
        PeopleWords = Set(Words(root, "peopleWords"));
        EmptyWords = Set(Words(root, "emptyWords"));
        FillerWords = Set(Words(root, "fillerWords"));
        MoodWords = root.GetProperty("moodWords").GetProperty("words").EnumerateObject()
            .ToDictionary(word => word.Name, word => word.Value.GetString()!, StringComparer.OrdinalIgnoreCase);

        using var wordNet = Read("wordnet-words.json");
        FamilyWords = Set(wordNet.RootElement.GetProperty("family").EnumerateArray().Select(word => word.GetString()!));
        Adjectives = Set(wordNet.RootElement.GetProperty("adjectives").EnumerateArray().Select(word => word.GetString()!));
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
