namespace APCS.Application.Abstractions.Moderation;

/// <summary>What the guardrail decided. Stored lower-case in <c>moderation_checks.decision</c>.</summary>
public enum ModerationDecision
{
    /// <summary>Nothing found, or only a low-severity concern.</summary>
    Allow,

    /// <summary>A medium-severity concern: let through, but recorded so the policy can be tuned.</summary>
    Flagged,

    /// <summary>Refused under the platform's content policy.</summary>
    Block,

    /// <summary>The check could not run (Gemini unavailable). An output image in this state cannot be approved.</summary>
    Unchecked
}

/// <summary>How serious a concern in one category is, as the classifier rates it.</summary>
public enum ModerationSeverity
{
    None,
    Low,
    Medium,
    High
}

public enum ModerationDirection
{
    /// <summary>Text on its way to a provider.</summary>
    Input,

    /// <summary>An image a provider returned.</summary>
    Output
}

/// <summary>The policy categories, as stored in <c>moderation_terms.category</c> and the classifier's answer.</summary>
public static class ModerationCategories
{
    public const string Sexual = "sexual";
    public const string SexualMinors = "sexual_minors";
    public const string ViolenceGore = "violence_gore";
    public const string Hate = "hate";
    public const string SelfHarm = "self_harm";
    public const string Illegal = "illegal";
    public const string IpTrademark = "ip_trademark";

    public static IReadOnlyList<string> All { get; } =
        [Sexual, SexualMinors, ViolenceGore, Hate, SelfHarm, Illegal, IpTrademark];

    /// <summary>A short name shown to the seller.</summary>
    public static string Label(string category) => category switch
    {
        Sexual => "sexual content",
        SexualMinors => "sexual content involving minors",
        ViolenceGore => "graphic violence or gore",
        Hate => "hateful content",
        SelfHarm => "self-harm",
        Illegal => "illegal goods or activity",
        IpTrademark => "a protected brand, character or trademark",
        _ => category
    };
}

/// <summary>The features the guardrail runs in (<c>moderation_checks.feature</c>).</summary>
public static class ModerationFeatures
{
    public const string DesignGeneration = "design_generation";
    public const string AiStudioFrame = "ai_studio_frame";
    public const string AiVideo = "ai_video";
}

/// <summary>What a check was about (<c>moderation_checks.subject_type</c>).</summary>
public static class ModerationSubjects
{
    public const string Batch = "batch";
    public const string Product = "product";
    public const string ProductPrompt = "product_prompt";
    public const string BatchJob = "batch_job";
    public const string DesignTemplate = "design_template";
    public const string StylePreset = "style_preset";
    public const string Workflow = "workflow";
    public const string AiShot = "ai_shot";
    public const string StudioFramePrompt = "studio_frame_prompt";
    public const string DesignImage = "design_image";
    public const string MockupImage = "mockup_image";
}

/// <summary>Who decided (<c>moderation_checks.decided_by</c>).</summary>
public static class ModerationDeciders
{
    public const string Blocklist = "blocklist";
    public const string Gemini = "gemini";
    public const string ProviderSafety = "provider_safety";
    public const string Error = "error";
    public const string Disabled = "disabled";
}

/// <summary>One untrusted text field, named so a finding can say which field it was in.</summary>
public sealed record ModerationText(string Field, string? Value);

/// <param name="UserId">The seller the content belongs to, when known.</param>
/// <param name="Feature">One of <see cref="ModerationFeatures"/>.</param>
/// <param name="Direction">Input text or output image.</param>
/// <param name="SubjectType">One of <see cref="ModerationSubjects"/>.</param>
/// <param name="SubjectId">The row checked, when it has one yet.</param>
public sealed record ModerationContext(
    Guid? UserId,
    string Feature,
    ModerationDirection Direction,
    string SubjectType,
    Guid? SubjectId = null);

/// <summary>A concern found in one category.</summary>
/// <param name="Category">One of <see cref="ModerationCategories"/>.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Field">The text field it was found in, when known.</param>
/// <param name="Evidence">The words or visual element behind it, kept short.</param>
public sealed record ModerationFinding(string Category, ModerationSeverity Severity, string? Field = null, string? Evidence = null);

/// <summary>The outcome of one check.</summary>
/// <param name="Decision">What was decided.</param>
/// <param name="Findings">The concerns behind it; empty when nothing was found.</param>
/// <param name="DecidedBy">One of <see cref="ModerationDeciders"/>.</param>
/// <param name="Reason">A short, seller-safe explanation, when there is one.</param>
public sealed record ModerationVerdict(
    ModerationDecision Decision,
    IReadOnlyList<ModerationFinding> Findings,
    string DecidedBy,
    string? Reason = null)
{
    public bool IsBlocked => Decision == ModerationDecision.Block;

    public static ModerationVerdict Allowed(string decidedBy) => new(ModerationDecision.Allow, [], decidedBy);

    public static ModerationVerdict NotChecked(string reason) =>
        new(ModerationDecision.Unchecked, [], ModerationDeciders.Error, reason);
}
