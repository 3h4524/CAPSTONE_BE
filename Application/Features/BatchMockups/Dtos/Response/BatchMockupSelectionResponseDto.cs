namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>The mock-up selection stored on a batch.</summary>
/// <param name="BatchJobId">The batch id.</param>
/// <param name="TemplateIds">The selected template ids.</param>
/// <param name="GarmentColors">The garment colors shared by templates that have none of their own.</param>
/// <param name="TemplateColors">The garment colors chosen per template, keyed by template id.</param>
public sealed record BatchMockupSelectionResponseDto(
    Guid BatchJobId,
    IReadOnlyList<Guid> TemplateIds,
    IReadOnlyList<string> GarmentColors,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>>? TemplateColors = null);
