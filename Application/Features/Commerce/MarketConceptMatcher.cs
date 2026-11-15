namespace APCS.Application.Features.Commerce;

/// <summary>
/// Groups the listings of a niche into product concepts by their Etsy tags. Sellers tag a listing with what it is
/// ("math pun shirt", "first day of school"), so two listings sharing several tags that are not common to the whole
/// niche show the same concept even when their long titles differ. Titles still match as before for listings with
/// few tags.
/// </summary>
public static class MarketConceptMatcher
{
    /// <summary>Distinctive tags two listings must share to be the same concept.</summary>
    public const int SharedTagsForSameConcept = 3;

    /// <summary>Title similarity that makes two listings the same concept on its own.</summary>
    public const double SameTitleSimilarity = 0.6;

    /// <summary>A tag on at least this share of the niche's collected listings says the niche, not a concept.</summary>
    public const double CommonTagShare = 0.15;

    /// <summary>Listings a niche needs in a collection before its common tags can be told apart.</summary>
    public const int ListingsToFindCommonTags = 20;

    /// <summary>A product concept of a niche: its opportunity, title and the distinctive tags its top listings carry.</summary>
    public sealed class Concept(Guid opportunityId, string title, IEnumerable<string> tags)
    {
        public Guid OpportunityId { get; } = opportunityId;
        public string Title { get; } = title;
        public HashSet<string> Tags { get; } = new(tags, StringComparer.Ordinal);
    }

    public static string NormalizeTag(string tag) =>
        string.Join(' ', tag.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>
    /// The tags that say the niche rather than a concept: the niche phrase itself and, with enough listings collected,
    /// the tags on <see cref="CommonTagShare"/> of them or more.
    /// </summary>
    public static HashSet<string> CommonTags(string niche, IReadOnlyCollection<IEnumerable<string>> listingTags)
    {
        var common = new HashSet<string>(StringComparer.Ordinal) { NormalizeTag(niche) };
        if (listingTags.Count < ListingsToFindCommonTags)
            return common;
        var threshold = listingTags.Count * CommonTagShare;
        foreach (var (tag, count) in listingTags
                     .SelectMany(tags => tags.Select(NormalizeTag).Distinct())
                     .GroupBy(tag => tag)
                     .Select(group => (group.Key, group.Count())))
        {
            if (count >= threshold)
                common.Add(tag);
        }
        return common;
    }

    /// <summary>A listing's tags that can tell a concept apart: normalized, without the niche's common tags.</summary>
    public static HashSet<string> DistinctiveTags(IEnumerable<string> tags, IReadOnlySet<string> common) =>
        tags.Select(NormalizeTag).Where(tag => tag.Length > 0 && !common.Contains(tag)).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The concept a listing belongs to, or null: the one sharing the most distinctive tags (at least
    /// <see cref="SharedTagsForSameConcept"/>), else one whose title is nearly the same.
    /// </summary>
    public static Concept? Match(string title, IReadOnlySet<string> tags, IEnumerable<Concept> concepts)
    {
        Concept? best = null;
        var bestShared = 0;
        var bestSimilarity = 0d;
        foreach (var concept in concepts)
        {
            var shared = concept.Tags.Count(tags.Contains);
            var similarity = string.Equals(concept.Title, title, StringComparison.OrdinalIgnoreCase)
                ? 1d
                : MarketOpportunityConceptMatcher.Similarity(concept.Title, title);
            if (shared < SharedTagsForSameConcept && similarity < SameTitleSimilarity)
                continue;
            if (shared > bestShared || (shared == bestShared && similarity > bestSimilarity))
            {
                best = concept;
                bestShared = shared;
                bestSimilarity = similarity;
            }
        }
        return best;
    }
}
