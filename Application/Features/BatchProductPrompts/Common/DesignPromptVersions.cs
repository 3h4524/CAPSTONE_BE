namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>
/// The versions of the rules a design image prompt is built with, so the admin pages can tell images apart by the
/// prompt that made them. Every new design image carries <see cref="Current"/> in its generation metadata; images made
/// before that are placed by the day they were made. Add a version and point <see cref="Current"/> at it whenever
/// <see cref="ArtworkPromptRules"/> or the way a design request is put together changes what the image model is asked.
/// </summary>
public static class DesignPromptVersions
{
    /// <summary>The template prompt from before the rule-based rewrite.</summary>
    public const string Original = "v0-original";

    /// <summary>Rule-based prompts with a lettering instruction, first used with Ideogram.</summary>
    public const string LetteringRules = "v1-lettering-rules";

    /// <summary>The prompt rule fixes that came with regenerate options and blocked-design reasons.</summary>
    public const string RuleFixes = "v2-rule-fixes";

    /// <summary>Keywords sorted with WordNet data, and the illustration placed above or below the lettering.</summary>
    public const string KeywordRules = "v3-keyword-rules";

    /// <summary>
    /// Keywords sent as written less product and listing words, product words taken out of the whole product name, and
    /// edge-to-edge prints from no template asked for the artwork itself.
    /// </summary>
    public const string KeywordsAsWritten = "v4-keywords-as-written";

    /// <summary>The version every new design image is labelled with.</summary>
    public const string Current = KeywordsAsWritten;

    /// <summary>
    /// An image made after labelling started that carries no version: code from before the label, still running
    /// somewhere against the shared database, made it, so its date says nothing about its prompt.
    /// </summary>
    public const string Unlabeled = "unlabeled";

    /// <summary>When new images started to carry their prompt version.</summary>
    public static DateTime LabelingStartedUtc { get; } = new(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Every version, oldest first, with the moment it was first used. The moments come from the images in the
    /// database rather than from commit times: code ran before it was committed.
    /// </summary>
    public static IReadOnlyList<DesignPromptVersion> All { get; } =
    [
        new(Original, "Original prompt", null),
        new(LetteringRules, "Rule-based lettering prompt", new DateTime(2026, 10, 20, 17, 0, 0, DateTimeKind.Utc)),
        new(RuleFixes, "Prompt rule fixes", new DateTime(2026, 10, 25, 17, 0, 0, DateTimeKind.Utc)),
        new(KeywordRules, "WordNet keyword rules", new DateTime(2026, 10, 30, 14, 30, 0, DateTimeKind.Utc)),
        new(KeywordsAsWritten, "Keywords as written", new DateTime(2026, 11, 1, 14, 30, 0, DateTimeKind.Utc)),
    ];

    /// <summary>
    /// The version an image belongs to: the one it carries, otherwise <see cref="Unlabeled"/> when it was made after
    /// labelling started, otherwise the version in use on the day it was made.
    /// </summary>
    public static string Resolve(string? label, DateTime createdAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        return createdAtUtc >= LabelingStartedUtc ? Unlabeled : AtDate(createdAtUtc);
    }

    /// <summary>The version in use at a moment, for records that never carry a label, such as job logs.</summary>
    public static string AtDate(DateTime momentUtc) =>
        All.Last(version => version.SinceUtc is null || version.SinceUtc <= momentUtc).Key;

    /// <summary>A version's place in time, oldest first; unknown and unlabelled versions come last.</summary>
    public static int OrderOf(string key)
    {
        for (var index = 0; index < All.Count; index++)
        {
            if (All[index].Key == key)
            {
                return index;
            }
        }

        return key == Unlabeled ? All.Count + 1 : All.Count;
    }
}

/// <summary>One version of the design prompt rules.</summary>
/// <param name="Key">The label stored with an image.</param>
/// <param name="Label">What the admin pages call it.</param>
/// <param name="SinceUtc">When it was first used; null for the first version.</param>
public sealed record DesignPromptVersion(string Key, string Label, DateTime? SinceUtc);
