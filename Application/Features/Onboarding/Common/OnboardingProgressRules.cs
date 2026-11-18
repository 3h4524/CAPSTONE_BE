using System.Text.RegularExpressions;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Onboarding.Dtos.Request;
using APCS.Common.Constants;

namespace APCS.Application.Features.Onboarding.Common;

/// <summary>
/// Shared rules for onboarding keys and guide entries: short slugs, deduplicated, bounded in count.
/// </summary>
internal static partial class OnboardingProgressRules
{
    /// <summary>How many guides at most one state may carry.</summary>
    public const int MaxGuides = 20;

    private const int MaxKeyLength = 64;
    private const int MaxVersion = 1000;
    private const int MaxStep = 500;

    /// <summary>The state of a user who has never saved anything.</summary>
    public static readonly OnboardingProgressState EmptyState =
        new(new Dictionary<string, GuideProgress>(StringComparer.Ordinal), false);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex GuideKeyPattern();

    /// <summary>
    /// Checks every guide key, its status, its version and its step, collecting one error per field.
    /// </summary>
    public static bool TryValidate(
        UpdateOnboardingProgressRequest request,
        out IReadOnlyDictionary<string, string[]> errorDetails)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.GuideProgress is null)
        {
            errors[nameof(request.GuideProgress)] = ["The collection is required."];
        }
        else if (request.GuideProgress.Count > MaxGuides)
        {
            errors[nameof(request.GuideProgress)] = [$"At most {MaxGuides} guides are allowed."];
        }
        else
        {
            foreach (var (key, progress) in request.GuideProgress)
            {
                ValidateGuide(key, progress, errors);
            }
        }

        errorDetails = errors;
        return errors.Count == 0;
    }

    private static void ValidateGuide(
        string key,
        Dtos.GuideProgressDto? progress,
        IDictionary<string, string[]> errors)
    {
        var field = $"{nameof(UpdateOnboardingProgressRequest.GuideProgress)}.{key}";

        if (key is null
            || key.Length == 0
            || key.Length > MaxKeyLength
            || !GuideKeyPattern().IsMatch(key))
        {
            errors[field] = ["Guide keys must be lower-case slugs (a-z, 0-9, dash), at most 64 characters."];
            return;
        }

        if (progress is null)
        {
            errors[field] = ["The guide entry is required."];
            return;
        }

        if (!OnboardingGuideStatuses.IsKnown(progress.Status))
        {
            errors[field] = ["Status must be active, completed or skipped."];
            return;
        }

        if (progress.Version < 1 || progress.Version > MaxVersion)
        {
            errors[field] = [$"Version must be between 1 and {MaxVersion}."];
            return;
        }

        if (progress.Step < 0 || progress.Step > MaxStep)
        {
            errors[field] = [$"Step must be between 0 and {MaxStep}."];
        }
    }
}
