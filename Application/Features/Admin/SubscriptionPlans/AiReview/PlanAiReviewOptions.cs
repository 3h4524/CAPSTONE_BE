namespace APCS.Application.Features.Admin.SubscriptionPlans.AiReview;

/// <summary>
/// When the AI review of the paid plans is refreshed and which Gemini model writes it, bound from
/// the "PlanAiReview" configuration section.
/// </summary>
public sealed class PlanAiReviewOptions
{
    public const string SectionName = "PlanAiReview";

    /// <summary>False stops the background job; a review already stored is still shown.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Days a successful review is kept before the job writes a new one.</summary>
    public int RefreshIntervalDays { get; set; } = 14;

    /// <summary>Hours the job waits after a failed attempt before trying again.</summary>
    public int RetryAfterFailureHours { get; set; } = 24;

    /// <summary>
    /// Hours an Admin waits between two reviews they ask for; 0 lets them ask any time (one review is
    /// still written at a time). Raise it if the Gemini plan bills per request.
    /// </summary>
    public int ManualRegenerateCooldownHours { get; set; }

    public string Model { get; set; } = "gemini-3.5-flash";

    /// <summary>Tried when <see cref="Model"/> is overloaded; the same model again, since gemini-3.8-flash did not answer in time.</summary>
    public string FallbackModel { get; set; } = "gemini-3.5-flash";

    /// <summary>A review takes about four minutes, far longer than a listing, so it has its own limit.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 300;

    /// <summary>Generations tried when an answer fails validation, before the attempt is stored as failed.</summary>
    public int MaxGenerations { get; set; } = 2;

    public bool IsValid() =>
        RefreshIntervalDays is >= 1 and <= 90
        && RetryAfterFailureHours is >= 1 and <= 168
        && ManualRegenerateCooldownHours is >= 0 and <= 168
        && !string.IsNullOrWhiteSpace(Model)
        && AttemptTimeoutSeconds is >= 30 and <= 900
        && MaxGenerations is >= 1 and <= 3;
}
