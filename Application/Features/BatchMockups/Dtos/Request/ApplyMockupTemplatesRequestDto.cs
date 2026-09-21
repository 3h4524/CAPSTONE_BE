namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>Applies mock-up templates to a batch.</summary>
/// <param name="TemplateIds">The selected template ids, between 1 and 5 distinct values.</param>
public sealed record ApplyMockupTemplatesRequestDto(IReadOnlyList<Guid> TemplateIds);
