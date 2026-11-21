namespace APCS.Application.Features.Onboarding.Dtos.Response;

/// <summary>
/// The confirmed first steps behind the dashboard checklist.
/// </summary>
public sealed record OnboardingSignalsResponse(
    bool ApiKeyConnected,
    bool BatchCreated,
    bool MockupTemplateUploaded,
    bool WorkflowRunStarted);
