namespace APCS.Application.Features.Onboarding.Dtos;

/// <summary>
/// One guide's progress in the onboarding state.
/// </summary>
public sealed record GuideProgressDto(
    int Version,
    string Status,
    int Step);
