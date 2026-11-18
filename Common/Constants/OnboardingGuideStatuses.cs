namespace APCS.Common.Constants;

/// <summary>
/// The values of a guide's status in the Seller's onboarding progress.
/// </summary>
public static class OnboardingGuideStatuses
{
    /// <summary>The Seller left the guide midway; it resumes at the saved step.</summary>
    public const string Active = "active";

    /// <summary>The Seller reached the last step.</summary>
    public const string Completed = "completed";

    /// <summary>The Seller skipped the guide; it does not open by itself again for this version.</summary>
    public const string Skipped = "skipped";

    /// <summary>
    /// Whether <paramref name="status"/> is one of the statuses above.
    /// </summary>
    public static bool IsKnown(string? status) =>
        status is Active or Completed or Skipped;
}
