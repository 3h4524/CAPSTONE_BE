namespace APCS.Application.Abstractions.Persistence;

/// <summary>
/// The Seller's onboarding state, from <c>user_onboarding_progress</c>, and the signals behind the
/// dashboard checklist. The progress table is added by a Flyway migration, not scaffolded, so reads
/// and writes go through plain SQL and tolerate the table not existing yet.
/// </summary>
public interface IOnboardingProgressRepository
{
    /// <summary>
    /// The row for <paramref name="userId"/>, or null when there is none yet (or the table does not exist yet).
    /// </summary>
    Task<OnboardingProgressState?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts the row for <paramref name="userId"/>. False when the table does not exist yet.
    /// </summary>
    Task<bool> SaveAsync(Guid userId, OnboardingProgressState progress, CancellationToken cancellationToken);

    /// <summary>
    /// Which first steps the Seller has actually finished, read from the tables those steps write to.
    /// </summary>
    Task<OnboardingSignals> GetSignalsAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// The persisted onboarding state: each guide's version, status and step to resume at, and whether
/// the Seller hid the dashboard checklist.
/// </summary>
public sealed record OnboardingProgressState(
    IReadOnlyDictionary<string, GuideProgress> GuideProgress,
    bool ChecklistDismissed);

/// <summary>
/// One guide's progress: the version it belongs to, its status, and the step left off at.
/// </summary>
public sealed record GuideProgress(int Version, string Status, int Step);

/// <summary>
/// The confirmed first steps, so the dashboard checklist reflects work actually done rather than clicks.
/// </summary>
public sealed record OnboardingSignals(
    bool ApiKeyConnected,
    bool BatchCreated,
    bool MockupTemplateUploaded,
    bool WorkflowRunStarted)
{
    /// <summary>Every step still to do, for a user with no work yet.</summary>
    public static readonly OnboardingSignals Empty = new(false, false, false, false);
}
