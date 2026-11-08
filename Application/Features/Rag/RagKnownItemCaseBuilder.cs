using System.Security.Cryptography;
using System.Text;
using APCS.Application.Features.Rag.Common;
using APCS.Application.Features.Rag.Dtos;

namespace APCS.Application.Features.Rag;

/// <summary>
/// Builds known-item retrieval cases from listing documents. A query is written from a listing's own
/// niche, tags and description, so the listing itself is the expected answer and no manual labelling
/// is needed. Each listing gives two cases: a short query the way a seller searches, and a long query
/// in the shape the listing generator sends (niche; keywords; description).
/// </summary>
public static class RagKnownItemCaseBuilder
{
    public const string ShortCategory = "known-item-short";
    public const string LongCategory = "known-item-long";
    public const int TopK = 10;

    private const int ShortQueryTags = 3;
    private const int LongQueryTags = 8;
    private const int LongQueryDescriptionLength = 200;

    public static IReadOnlyList<RagEvaluationCaseRequest> Build(
        IEnumerable<RagKnownItemSource> sources,
        int sampleSize,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return sources
            .Select(source => (Source: source, Tags: Tags(source.Content), Description: Field(source.Content, "Description:")))
            // Without tags or a description the query would be the niche alone, which many listings share.
            .Where(item => item.Tags.Count > 0 || item.Description.Length > 0)
            .GroupBy(item => item.Source.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => SampleKey(seed, item.Source.SourceId), StringComparer.Ordinal)
            .Take(Math.Max(0, sampleSize))
            .SelectMany(item => new[]
            {
                Case(item.Source, ShortCategory, ShortQuery(item.Source.Niche, item.Tags)),
                Case(item.Source, LongCategory, LongQuery(item.Source.Niche, item.Tags, item.Description))
            })
            .ToArray();
    }

    private static RagEvaluationCaseRequest Case(RagKnownItemSource source, string category, string query) => new(
        $"{source.SourceId}#{(category == ShortCategory ? "short" : "long")}",
        new RagSearchRequest(query, TopK, ContentType: RagContentTypes.Listing),
        ExpectedSourceId: source.SourceId,
        Category: category,
        ContentType: RagContentTypes.Listing);

    private static string ShortQuery(string? niche, IReadOnlyList<string> tags) =>
        string.Join(' ', new[] { niche ?? string.Empty }.Concat(tags.Take(ShortQueryTags))
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string LongQuery(string? niche, IReadOnlyList<string> tags, string description) =>
        $"{niche}; {string.Join(", ", tags.Take(LongQueryTags))}; {description[..Math.Min(LongQueryDescriptionLength, description.Length)]}";

    private static IReadOnlyList<string> Tags(string content) => Field(content, "Tags:")
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string Field(string content, string prefix) => content
        .Split('\n')
        .Select(line => line.Trim())
        .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim() ?? string.Empty;

    /// <summary>A stable per-seed order: the same seed always samples the same listings, on any machine.</summary>
    private static string SampleKey(int seed, string sourceId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{sourceId}")));
}
