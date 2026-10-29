namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>
/// Makes several mock-ups of a batch job their own way at once, such as one change copied onto the job's other
/// mock-ups. Every change is checked first and none is kept unless all are, and the job's mock-ups are made once.
/// </summary>
/// <param name="Overrides">One change per mock-up, each named by its slot key; a mock-up appears at most once.</param>
public sealed record SaveMockupOverridesRequestDto(IReadOnlyList<SaveMockupOverrideRequestDto> Overrides);
