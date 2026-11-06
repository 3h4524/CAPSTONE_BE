using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using APCS.Application.Abstractions.AI;

namespace APCS.Application.Features.DesignGeneration.Common;

/// <summary>
/// The text a design was asked to print, compared with the text OCR read in it: the same rules as the
/// offline scoring (ocr.py, kept outside the repo), so the app and the report count a correct design the same way.
/// </summary>
/// <param name="Ok">Every word of the phrase is there, and there is no other word and no repeated one.</param>
/// <param name="Expected">The phrase the design prints; null when it should print no words.</param>
/// <param name="Read">What OCR read, line by line, joined with " / ".</param>
/// <param name="Missing">Words of the phrase OCR did not find.</param>
/// <param name="Extra">Words that are not in the phrase.</param>
/// <param name="Repeated">Words of the phrase found more than once.</param>
/// <param name="CharacterErrorRate">Edits between the phrase and the text read, per character of the phrase; null without a phrase.</param>
public sealed record DesignTextCheckResult(
    bool Ok,
    string? Expected,
    string Read,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Extra,
    IReadOnlyList<string> Repeated,
    double? CharacterErrorRate);

public static partial class DesignTextCheck
{
    /// <summary>The key the result is kept under in a design image's generation metadata.</summary>
    public const string MetadataKey = "textCheck";

    /// <summary>OCR lines read with less confidence than this are ignored.</summary>
    public const double MinimumScore = 0.5;

    // A single stray character from an illustration is not counted as an extra word.
    private const int MinimumExtraLength = 2;

    /// <summary>Compares the phrase a design should print with the lines OCR read in it.</summary>
    /// <param name="expected">The phrase; null or blank when the design should print no words.</param>
    public static DesignTextCheckResult Compare(string? expected, IReadOnlyList<DesignTextLine> lines)
    {
        var kept = lines.Where(line => line.Score >= MinimumScore && line.Text.Trim().Length > 0).ToList();
        var read = kept.SelectMany(line => Words(line.Text)).ToList();
        var phrase = Words(expected).ToList();

        var remaining = new List<string>(read);
        var missing = new List<string>();
        var matched = new List<string>();
        foreach (var word in phrase)
        {
            if (remaining.Remove(word)) matched.Add(word);
            else missing.Add(word);
        }

        // OCR sometimes glues neighbouring words ("FISHINGCLUB"): a missing word found inside a longer token counts.
        foreach (var word in missing.ToList())
        {
            if (remaining.Any(token => token.Length > word.Length && token.Contains(word, StringComparison.Ordinal)))
            {
                missing.Remove(word);
                matched.Add(word);
            }
        }

        var glued = remaining.Where(token => matched.Any(word => token.Length > word.Length && token.Contains(word, StringComparison.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);
        var extra = remaining.Where(token => !glued.Contains(token) && token.Length >= MinimumExtraLength && !phrase.Contains(token)).ToList();
        // The prompt asks for the phrase once: a second copy of one of its words is a repeat.
        var repeated = remaining.Where(phrase.Contains).ToList();

        double? characterErrorRate = null;
        if (phrase.Count > 0)
        {
            var phraseChars = string.Concat(phrase);
            characterErrorRate = Math.Round((double)EditDistance(phraseChars, string.Concat(read)) / phraseChars.Length, 4);
        }

        return new DesignTextCheckResult(
            missing.Count == 0 && extra.Count == 0 && repeated.Count == 0,
            string.IsNullOrWhiteSpace(expected) ? null : expected.Trim(),
            string.Join(" / ", kept.Select(line => line.Text.Trim())),
            missing, extra, repeated, characterErrorRate);
    }

    /// <summary>The result as kept in a design image's generation metadata.</summary>
    public static JsonObject ToJson(DesignTextCheckResult result)
    {
        var json = new JsonObject
        {
            ["ok"] = result.Ok,
            ["expected"] = result.Expected,
            ["read"] = result.Read,
            ["missing"] = new JsonArray(result.Missing.Select(word => (JsonNode?)word).ToArray()),
            ["extra"] = new JsonArray(result.Extra.Select(word => (JsonNode?)word).ToArray()),
            ["repeated"] = new JsonArray(result.Repeated.Select(word => (JsonNode?)word).ToArray())
        };
        if (result.CharacterErrorRate is { } rate) json["cer"] = rate;
        return json;
    }

    /// <summary>A design image's generation metadata with the result added (or replaced); other keys are kept.</summary>
    public static string WithResult(string? metadata, DesignTextCheckResult result)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(metadata) ? new JsonObject() : JsonNode.Parse(metadata) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }
        root[MetadataKey] = ToJson(result);
        return root.ToJsonString();
    }

    /// <summary>The result kept in a design image's generation metadata, or null when its text was not checked.</summary>
    public static DesignTextCheckResult? FromMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return null;
        try
        {
            if (JsonNode.Parse(metadata)?[MetadataKey] is not JsonObject check
                || check["ok"] is not JsonValue okValue || !okValue.TryGetValue<bool>(out var ok))
                return null;
            return new DesignTextCheckResult(
                ok,
                check["expected"] is JsonValue expected && expected.TryGetValue<string>(out var phrase) ? phrase : null,
                check["read"] is JsonValue read && read.TryGetValue<string>(out var text) ? text : string.Empty,
                Strings(check["missing"]), Strings(check["extra"]), Strings(check["repeated"]),
                check["cer"] is JsonValue cer && cer.TryGetValue<double>(out var rate) ? rate : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> Strings(JsonNode? node) =>
        node is JsonArray array
            ? array.OfType<JsonValue>().Select(value => value.TryGetValue<string>(out var text) ? text : null).OfType<string>().ToList()
            : [];

    // Upper case, split on anything that is not a letter or a digit: case and punctuation are not judged.
    private static IEnumerable<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : NotLetterOrDigit().Split(text.ToUpperInvariant()).Where(word => word.Length > 0);

    private static int EditDistance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[b.Length];
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotLetterOrDigit();
}
