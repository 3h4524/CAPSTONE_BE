namespace APCS.Application.Features.Onboarding.Dtos.Request;

/// <summary>
/// The full onboarding state to store; the client always sends every field.
/// </summary>
public sealed record UpdateOnboardingProgressRequest(
    IReadOnlyDictionary<string, GuideProgressDto> GuideProgress,
    bool ChecklistDismissed);
