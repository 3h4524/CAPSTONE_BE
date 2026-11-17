namespace APCS.Application.Features.Onboarding.Dtos.Response;

/// <summary>
/// The authenticated user's onboarding state and the confirmed steps behind the dashboard checklist.
/// </summary>
public sealed record OnboardingProgressResponse(
    IReadOnlyDictionary<string, GuideProgressDto> GuideProgress,
    bool ChecklistDismissed,
    OnboardingSignalsResponse Signals);
