using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using APCS.Application.Abstractions.Moderation;

namespace APCS.Application.Features.Moderation.Common;

/// <summary>A blocklist term found in a text.</summary>
public sealed record BlocklistHit(string Term, string Category, string Severity)
{
    public bool Blocks => Severity == BlocklistMatcher.BlockSeverity;
}

/// <summary>
/// Finds blocklist terms in a text, built once from the active lists. A term matches whole words of the
/// <see cref="TextNormalizer.Normalize">normalized</see> text: "sex" does not match "sussex", and the words of a phrase
/// may be split by any punctuation. Every letter may repeat ("nudeee"). Allowlisted phrases ("naked eye") are masked
/// first. A term marked <see cref="ModerationTermEntry.MatchUnaccented"/> is also matched once accents are removed.
/// </summary>
public sealed class BlocklistMatcher
{
    public const string BlockSeverity = "block";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private readonly ModerationTermEntry[] terms;
    private readonly Regex? accented;
    private readonly Regex? unaccented;
    private readonly Regex? allowed;
    private readonly Regex? allowedUnaccented;

    public BlocklistMatcher(IEnumerable<ModerationTermEntry> terms, IEnumerable<string> allowlist)
    {
        this.terms = terms
            .Where(term => !string.IsNullOrWhiteSpace(term.Term))
            .DistinctBy(term => term.Term)
            .OrderBy(term => term.Term, StringComparer.Ordinal)
            .ToArray();
        var normalized = this.terms.Select(term => TextNormalizer.Normalize(term.Term)).ToArray();
        accented = Build(normalized.Select((term, index) => (index, term)));
        unaccented = Build(normalized
            .Select((term, index) => (index, term: TextNormalizer.RemoveAccents(term)))
            .Where(item => this.terms[item.index].MatchUnaccented));

        var phrases = allowlist.Select(TextNormalizer.Normalize).Where(phrase => phrase.Length > 0).Distinct().ToArray();
        allowed = Build(phrases.Select((phrase, index) => (index, phrase)));
        allowedUnaccented = Build(phrases.Select((phrase, index) => (index, TextNormalizer.RemoveAccents(phrase))));
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            this.terms.Select(term => $"{term.Term}|{term.Category}|{term.Severity}|{term.MatchType}|{term.MatchUnaccented}")
                .Concat(phrases.Order(StringComparer.Ordinal).Select(phrase => "allow|" + phrase))))))[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Short hash of the lists the matcher was built from. Part of the cache key of a text verdict, so a verdict made
    /// under other lists is not reused once an administrator changes them.
    /// </summary>
    public string Fingerprint { get; }

    public static BlocklistMatcher Empty { get; } = new([], []);

    public IReadOnlyList<BlocklistHit> Match(string? text)
    {
        var normalized = TextNormalizer.Normalize(text);
        if (normalized.Length == 0 || terms.Length == 0) return [];

        var found = new HashSet<int>();
        Collect(accented, Mask(allowed, normalized), found);
        if (unaccented is not null)
            Collect(unaccented, Mask(allowedUnaccented, Mask(allowed, TextNormalizer.RemoveAccents(normalized))), found);

        return found.Order()
            .Select(index => new BlocklistHit(terms[index].Term, terms[index].Category, terms[index].Severity))
            .ToArray();
    }

    private static void Collect(Regex? pattern, string text, HashSet<int> found)
    {
        if (pattern is null) return;
        try
        {
            foreach (Match match in pattern.Matches(text))
                foreach (Group group in match.Groups)
                    if (group.Success && group.Name.StartsWith('t') && int.TryParse(group.Name.AsSpan(1), out var index))
                        found.Add(index);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological text: the Gemini check still looks at it.
        }
    }

    private static string Mask(Regex? pattern, string text)
    {
        if (pattern is null) return text;
        try
        {
            return pattern.Replace(text, match => new string(' ', match.Length));
        }
        catch (RegexMatchTimeoutException)
        {
            return text;
        }
    }

    private static Regex? Build(IEnumerable<(int Index, string Text)> entries)
    {
        var alternatives = entries
            .Select(entry => (entry.Index, Pattern: WordsPattern(entry.Text)))
            .Where(entry => entry.Pattern.Length > 0)
            .Select(entry => $"(?<t{entry.Index}>{entry.Pattern})")
            .ToArray();
        if (alternatives.Length == 0) return null;
        return new Regex(
            $@"(?<![\p{{L}}\p{{N}}])(?:{string.Join('|', alternatives)})(?![\p{{L}}\p{{N}}])",
            Options,
            MatchTimeout);
    }

    // "child porn" becomes c+h+i+l+d+[^\p{L}\p{N}]+p+o+r+n+: any letter may repeat, any non-letters split the words.
    private static string WordsPattern(string text)
    {
        var words = Regex.Split(text, @"[^\p{L}\p{N}]+").Where(word => word.Length > 0).ToArray();
        var builder = new StringBuilder();
        for (var index = 0; index < words.Length; index++)
        {
            if (index > 0) builder.Append(@"[^\p{L}\p{N}]+");
            foreach (var character in words[index])
                builder.Append(Regex.Escape(character.ToString())).Append('+');
        }
        return builder.ToString();
    }
}
