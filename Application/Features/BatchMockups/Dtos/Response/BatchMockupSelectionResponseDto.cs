namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>The mock-up selection stored on a batch.</summary>
/// <param name="BatchJobId">The batch id.</param>
/// <param name="TemplateIds">The selected template ids.</param>
public sealed record BatchMockupSelectionResponseDto(Guid BatchJobId, IReadOnlyList<Guid> TemplateIds);
