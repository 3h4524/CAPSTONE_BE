using System.Security.Cryptography;
using System.Text;
using APCS.Application.Abstractions.Moderation;

namespace APCS.Application.Features.Moderation.Common;

/// <summary>The keys of <c>moderation_settings</c>.</summary>
public static class ModerationSettingKeys
{
    public const string Enabled = "enabled";
    public const string BlockAtPrefix = "block_at.";

    /// <summary>The categories whose blocking threshold an administrator can change; minors always block.</summary>
    public static IReadOnlyList<string> AdjustableCategories { get; } =
        ModerationCategories.All.Where(category => category != ModerationCategories.SexualMinors).ToArray();

    public static string BlockAt(string category) => BlockAtPrefix + category;
}

/// <summary>
/// The guardrail settings an administrator can change: whether the Gemini check runs, and from which rating each
/// category blocks (high, or medium as well). <see cref="Fingerprint"/> is part of the cache key of a text verdict.
/// </summary>
public sealed record ModerationSettingsSnapshot(bool Enabled, IReadOnlyDictionary<string, ModerationSeverity> BlockAt)
{
    public static ModerationSettingsSnapshot Default { get; } = From(null);

    public string Fingerprint { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{Enabled}|" + string.Join('|', BlockAt.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => $"{item.Key}={item.Value}")))))[..16]
        .ToLowerInvariant();

    /// <summary>The lowest rating that blocks in a category; minors in a sexual context block at any rating.</summary>
    public ModerationSeverity BlockFrom(string category) =>
        category == ModerationCategories.SexualMinors ? ModerationSeverity.Low
        : BlockAt.TryGetValue(category, out var severity) ? severity
        : ModerationSeverity.High;

    /// <summary>Reads the stored values; a missing or unreadable value keeps its default (enabled, block from high).</summary>
    public static ModerationSettingsSnapshot From(IReadOnlyDictionary<string, string>? values)
    {
        var enabled = values is null || !values.TryGetValue(ModerationSettingKeys.Enabled, out var stored) ||
                      !bool.TryParse(stored, out var parsed) || parsed;
        var blockAt = ModerationSettingKeys.AdjustableCategories.ToDictionary(
            category => category,
            category => values is not null && values.TryGetValue(ModerationSettingKeys.BlockAt(category), out var value) &&
                        string.Equals(value, "medium", StringComparison.OrdinalIgnoreCase)
                ? ModerationSeverity.Medium
                : ModerationSeverity.High);
        return new ModerationSettingsSnapshot(enabled, blockAt);
    }
}
