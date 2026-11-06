using APCS.Common.Models;

namespace APCS.Application.Features.Entitlements;

/// <summary>
/// Errors raised when a Seller's plan does not cover a request. Every code starts with
/// <c>Plan.</c> so the client can offer an upgrade instead of a generic error.
/// </summary>
public static class PlanErrors
{
    public static Error NoActivePlan() =>
        Error.Forbidden("Plan.NoActivePlan", "Start your free 14-day Pro trial or choose a plan to run this.");

    /// <summary>The plan does not include the feature. <paramref name="label"/> is its catalog label, when known.</summary>
    public static Error FeatureLocked(string featureCode, string? label = null)
    {
        label ??= PlanFeatures.Find(featureCode)?.Label ?? featureCode;
        return Error.Forbidden("Plan.FeatureLocked", $"{label} is not included in your plan. Upgrade to Pro to use it.");
    }

    public static Error BatchTooLarge(int limit) =>
        Error.Forbidden("Plan.BatchTooLarge", $"Your plan runs up to {limit} products at a time. Split the batch or upgrade to Pro.");

    public static Error TooManyConcurrentJobs(int limit) =>
        Error.Conflict("Plan.TooManyConcurrentJobs", $"Your plan runs {limit} design job{(limit == 1 ? "" : "s")} at a time. Wait for a running job to finish, or upgrade to Pro.");

    public static Error TooManyVariations(int limit) =>
        Error.Forbidden("Plan.TooManyVariations", $"Your plan generates up to {limit} variation{(limit == 1 ? "" : "s")} per product. Choose fewer, or upgrade to Pro.");

    public static Error DailyCapReached(string what) =>
        Error.Conflict("Plan.DailyCapReached", $"You reached today's limit for {what}. It resets at midnight UTC.");

    /// <summary>Free or the Pro trial has too few of <paramref name="what"/> left for the request.</summary>
    public static Error AllowanceReached(string what, int limit, int remaining, string scope, DateOnly? resetsOn) =>
        Error.Conflict("Plan.AllowanceReached", scope == UsageAllowances.ScopeTrial
            ? $"Your Pro trial includes {limit} {what} and {remaining} {(remaining == 1 ? "is" : "are")} left. Choose a plan to keep going."
            : scope == UsageAllowances.ScopePlan
            ? $"Your plan includes {limit} {what} a month and {remaining} {(remaining == 1 ? "is" : "are")} left. They reset on {resetsOn?.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}, or upgrade to Pro for more."
            : $"Free includes {limit} {what} a month and {remaining} {(remaining == 1 ? "is" : "are")} left. They reset on {resetsOn?.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}, or upgrade to keep going.");

    public static Error MockupsPerRunExceeded(int limit, int requested) =>
        Error.Forbidden("Plan.MockupsPerRun", $"Free makes up to {limit} mockups per run and this run would make {requested}. Choose fewer templates or colours, or upgrade.");

    public static Error GeminiKeyRequired() =>
        new("Plan.GeminiKeyRequired", "Connect a working Gemini API key before you start the free Pro trial.", ErrorType.Validation);
}
