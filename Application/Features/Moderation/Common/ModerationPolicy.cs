using APCS.Application.Abstractions.Moderation;

namespace APCS.Application.Features.Moderation.Common;

/// <summary>
/// Turns the classifier's ratings into a decision. The thresholds live here and in <c>moderation_settings</c> rather than
/// in the prompt, so an administrator can change them without asking the model differently: any concern about minors
/// in a sexual context blocks; otherwise a rating at or above the category's threshold (high, or medium when set)
/// blocks, another medium rating is let through but flagged, and a low one is allowed.
/// </summary>
public static class ModerationPolicy
{
    public static ModerationDecision Decide(IEnumerable<ModerationFinding> findings) =>
        Decide(findings, ModerationSettingsSnapshot.Default);

    public static ModerationDecision Decide(IEnumerable<ModerationFinding> findings, ModerationSettingsSnapshot settings)
    {
        var list = findings.Where(finding => finding.Severity > ModerationSeverity.None).ToList();
        if (list.Any(finding => finding.Severity >= settings.BlockFrom(finding.Category))) return ModerationDecision.Block;
        if (list.Any(finding => finding.Severity == ModerationSeverity.Medium)) return ModerationDecision.Flagged;
        return ModerationDecision.Allow;
    }

    /// <summary>The seller-facing message for a blocked verdict.</summary>
    public static string BlockedMessage(ModerationVerdict verdict)
    {
        var categories = verdict.Findings
            .Where(finding => finding.Severity >= ModerationSeverity.Low)
            .OrderByDescending(finding => finding.Severity)
            .Select(finding => ModerationCategories.Label(finding.Category))
            .Distinct()
            .ToList();
        var field = verdict.Findings.Select(finding => finding.Field).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var what = categories.Count > 0 ? string.Join(", ", categories) : "content that is not allowed";
        var where = field is null ? string.Empty : $" (in {field})";
        return $"This content was blocked by the content policy because it appears to contain {what}{where}. Change it and try again.";
    }
}
