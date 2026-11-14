using System.ComponentModel.DataAnnotations;

namespace APCS.Application.Features.Moderation;

/// <summary>The content guardrail's settings (section <c>Moderation</c>). See docs/CONTENT_MODERATION.md.</summary>
public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";

    /// <summary>
    /// Runs the Gemini check. When false, or when <c>Gemini:TextGeneration:ApiKey</c> is not set, only the blocklist
    /// runs and output images are not checked.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The Gemini model that classifies (the platform's key pays for it).</summary>
    [Required]
    public string Model { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>How long one classification may take before the check counts as unavailable.</summary>
    [Range(2, 60)]
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Raised when the policy or prompt changes, so cached verdicts of the old policy are not reused.</summary>
    [Range(1, int.MaxValue)]
    public int PolicyVersion { get; set; } = 1;

    /// <summary>How long a text verdict is cached.</summary>
    [Range(0, 43200)]
    public int CacheMinutes { get; set; } = 1440;

    /// <summary>How often the blocklist is read again from the database.</summary>
    [Range(1, 1440)]
    public int ListRefreshMinutes { get; set; } = 10;
}
