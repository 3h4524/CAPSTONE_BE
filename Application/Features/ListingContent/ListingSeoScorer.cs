using System.Text.RegularExpressions;
using APCS.Application.Features.Shops.Common;

namespace APCS.Application.Features.ListingContent;

/// <summary>What <see cref="ListingSeoScorer"/> found in one listing. Section scores are out of 100;
/// <see cref="KeywordDensity"/> is the share of description words that are keyword words, in percent.</summary>
public sealed record ListingSeoEvaluation(
    decimal Overall,
    decimal Title,
    decimal Tags,
    decimal Description,
    decimal KeywordOptimization,
    decimal TagRelevance,
    decimal KeywordDensity,
    IReadOnlyList<string> Suggestions,
    string Recommendation,
    bool ShouldRegenerate);

/// <summary>
/// Scores a listing against Etsy search practice. Keywords are matched as phrases (or all of their words, singular
/// or plural), not one word at a time. A check that needs the product's keywords is left out, rather than given
/// free points, when the product has none.
/// </summary>
public static partial class ListingSeoScorer
{
    public const string Version = "v2";
    public const int IdealTitleMin = 40;
    public const int IdealTitleMax = 90;
    /// <summary>Above this share of description words, all keywords together read as stuffed.</summary>
    public const decimal MaxKeywordDensity = 5m;
    /// <summary>How far into the title the main keyword may start and still count as first.</summary>
    internal const int TitleLead = 40;
    internal const int DescriptionLead = 160;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "the", "for", "with", "of", "to", "in", "on", "or", "your", "my", "our", "his", "her", "by", "at", "is", "it", "this",
    };

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex NonWord();

    public static ListingSeoEvaluation Evaluate(string title, string description, IReadOnlyList<string> tags, IReadOnlyList<string> keywords)
    {
        var suggestions = new List<string>();
        var phrases = keywords.Select(Normalize).Where(phrase => phrase.Length > 0).Distinct().ToArray();
        var hasKeywords = phrases.Length > 0;
        var primary = hasKeywords ? phrases[0] : null;
        var cleanTags = tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0).ToArray();

        var titleScore = ScoreTitle(title, primary, suggestions);
        var tagsScore = ScoreTags(title, cleanTags, suggestions);
        var density = Density(description, phrases);
        var descriptionScore = ScoreDescription(description, primary, phrases, density, suggestions);

        decimal keywordScore = 0, tagRelevance = 0, overall;
        if (hasKeywords)
        {
            var listingText = $"{title} {description} {string.Join(' ', cleanTags)}";
            var covered = phrases.Count(phrase => Covers(listingText, phrase));
            keywordScore = Percent(covered, phrases.Length);
            if (keywordScore < 70)
                suggestions.Add($"Use more of the product keywords across the title, tags and description ({covered} of {phrases.Length} are there).");

            var keywordWords = phrases.SelectMany(Words).Select(Stem).ToHashSet();
            var relevant = cleanTags.Count(tag => Words(tag).Select(Stem).Any(keywordWords.Contains));
            // About 8 of 13 tags tied to the keywords is plenty; the rest should reach other searches.
            tagRelevance = Math.Min(100m, Percent(relevant, Math.Max(1, Math.Min(cleanTags.Length, 8))));
            if (tagRelevance < 60) suggestions.Add("Tie more tags to the product keywords.");
            overall = titleScore * .30m + tagsScore * .25m + descriptionScore * .20m + keywordScore * .15m + tagRelevance * .10m;
        }
        else
        {
            suggestions.Add("Add main keywords to the product: the keyword checks are skipped without them.");
            overall = titleScore * .40m + tagsScore * .35m + descriptionScore * .25m;
        }
        overall = Math.Round(overall, 2);

        var shouldRegenerate = overall < 70 || titleScore < 50 || tagsScore < 60;
        var recommendation = shouldRegenerate ? "regenerate" : overall >= 85 ? "keep" : "review";
        var distinct = suggestions.Distinct().ToList();
        if (distinct.Count == 0) distinct.Add("The listing meets the current SEO quality thresholds.");
        return new(overall, titleScore, tagsScore, descriptionScore, keywordScore, tagRelevance, density, distinct, recommendation, shouldRegenerate);
    }

    private static decimal ScoreTitle(string title, string? primary, List<string> suggestions)
    {
        var score = new Section(suggestions);
        var length = title.Trim().Length;
        if (length > EtsyListingText.MaxTitleLength)
            score.Add(30, 0, $"Etsy cuts titles at {EtsyListingText.MaxTitleLength} characters; shorten the title.");
        else if (length is >= IdealTitleMin and <= IdealTitleMax)
            score.Add(30, 1);
        else
            score.Add(30, length >= 20 ? .5m : 0, $"Keep the title between {IdealTitleMin} and {IdealTitleMax} characters: short and readable, main keyword first.");

        if (primary is not null)
        {
            var covered = Covers(title, primary);
            score.Add(25, covered ? 1 : 0, $"Put the main keyword \"{primary}\" in the title.");
            var first = Words(primary).FirstOrDefault() ?? primary;
            var position = $" {Normalize(title)} ".IndexOf($" {first}", StringComparison.Ordinal);
            score.Add(15, covered && position is >= 0 and <= TitleLead ? 1 : 0, $"Start the title with the main keyword \"{primary}\".");
        }

        var repeated = Words(title).Select(Stem).GroupBy(word => word)
            .Where(group => group.Key.Length > 2 && group.Count() > 2).Select(group => group.Key).ToArray();
        score.Add(15, repeated.Length == 0 ? 1 : 0, $"Do not repeat words in the title: {string.Join(", ", repeated)}.");

        var etsy = EtsyListingText.Title(title).Warnings;
        score.Add(15, etsy.Count == 0 ? 1 : 0, etsy.FirstOrDefault());
        return score.Total;
    }

    private static decimal ScoreTags(string title, IReadOnlyList<string> tags, List<string> suggestions)
    {
        var score = new Section(suggestions);
        score.Add(15, Math.Min(1m, tags.Count / (decimal)EtsyListingText.MaxTags), $"Use all {EtsyListingText.MaxTags} tags.");

        var valid = tags.All(tag => tag.Length <= EtsyListingText.MaxTagLength)
            && tags.Distinct(StringComparer.OrdinalIgnoreCase).Count() == tags.Count;
        score.Add(10, valid ? 1 : 0, $"Keep tags unique and at most {EtsyListingText.MaxTagLength} characters.");

        // Buyers search in phrases: most tags should be two words or more.
        var multiWord = tags.Count(tag => Words(tag).Count() >= 2);
        score.Add(25, Math.Min(1m, multiWord / 8m), "Use multi-word tags (for example \"camping gift dad\"), not single words.");

        // Title words are already searchable; tags reach further when they add other words.
        var titleWords = Words(title).Select(Stem).ToHashSet();
        var adding = tags.Count(tag => Words(tag).Select(Stem).Any(word => !titleWords.Contains(word)));
        score.Add(30, Math.Min(1m, adding / 6m), "Use tags for searches the title does not cover: style, recipient, occasion, theme.");

        var overused = tags.SelectMany(tag => Words(tag).Select(Stem).Distinct()).GroupBy(word => word)
            .Where(group => group.Count() > 4).Select(group => group.Key).ToArray();
        score.Add(20, overused.Length == 0 ? 1 : 0, $"Vary the tags: \"{string.Join("\", \"", overused)}\" appears in more than four of them.");
        return score.Total;
    }

    private static decimal ScoreDescription(string description, string? primary, IReadOnlyList<string> phrases, decimal density, List<string> suggestions)
    {
        var score = new Section(suggestions);
        var words = Words(description).Count();
        var lengthShare = words >= 120 ? 1m : words >= 80 ? .7m : words >= 40 ? .4m : .1m;
        score.Add(25, lengthShare, "Expand the description with useful buyer-facing details (aim for 120+ words) without inventing claims.");

        var paragraphs = description.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Count(part => part.Trim().Length > 0);
        var lines = description.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(line => line.Trim().Length > 0);
        score.Add(15, paragraphs >= 2 || lines >= 3 ? 1 : 0, "Split the description into short paragraphs or a bullet list.");

        if (primary is not null)
        {
            var lead = description.Length <= DescriptionLead ? description : description[..DescriptionLead];
            score.Add(25, Covers(lead, primary) ? 1 : 0, $"Mention \"{primary}\" in the first sentence of the description.");
            score.Add(20, Percent(phrases.Count(phrase => Covers(description, phrase)), phrases.Count) / 100m, "Work more of the product keywords into the description.");
            score.Add(15, density <= MaxKeywordDensity ? 1 : 0, $"The keywords make up {density}% of the description; keep it at or below {MaxKeywordDensity}% so it reads naturally.");
        }
        return score.Total;
    }

    /// <summary>Share of description words that are keyword words, in percent.</summary>
    private static decimal Density(string description, IReadOnlyList<string> phrases)
    {
        var words = Normalize(description).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Stem).ToArray();
        if (words.Length == 0 || phrases.Count == 0) return 0;
        var keywordWords = phrases.SelectMany(Words).Select(Stem).ToHashSet();
        return Math.Round(words.Count(keywordWords.Contains) * 100m / words.Length, 2);
    }

    /// <summary>True when the text has the phrase, or every meaningful word of it in singular or plural.</summary>
    internal static bool Covers(string text, string phrase)
    {
        var normalized = Normalize(phrase);
        if (normalized.Length == 0) return false;
        if ($" {Normalize(text)} ".Contains($" {normalized} ", StringComparison.Ordinal)) return true;
        var words = Words(text).Select(Stem).ToHashSet();
        var needed = Words(normalized).Select(Stem).ToArray();
        return needed.Length > 0 && needed.All(words.Contains);
    }

    internal static string Normalize(string value) => NonWord().Replace(value.ToLowerInvariant(), " ").Trim();

    private static IEnumerable<string> Words(string value) =>
        Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => !StopWords.Contains(word));

    private static string Stem(string word) =>
        word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1] : word;

    private static decimal Percent(int part, int whole) => whole == 0 ? 0 : Math.Round(part * 100m / whole, 2);

    /// <summary>Points of the checks that apply, scaled to 100; a check not fully met adds its suggestion.</summary>
    private sealed class Section(List<string> suggestions)
    {
        private decimal earned, possible;

        public void Add(decimal points, decimal share, string? suggestion = null)
        {
            possible += points;
            earned += points * Math.Clamp(share, 0, 1);
            if (share < 1 && suggestion is not null) suggestions.Add(suggestion);
        }

        public decimal Total => possible == 0 ? 0 : Math.Round(earned * 100 / possible, 2);
    }
}
