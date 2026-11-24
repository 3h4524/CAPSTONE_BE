namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>The mock-up selection stored on a batch.</summary>
/// <param name="BatchJobId">The batch id.</param>
/// <param name="TemplateIds">The selected template ids.</param>
/// <param name="GarmentColors">The garment colors shared by templates that have none of their own.</param>
/// <param name="TemplateColors">The garment colors chosen per template, keyed by template id.</param>
/// <param name="AreaSources">What is printed in the print areas of the selected templates, keyed by template id and then area key.</param>
/// <param name="TemplateBackgrounds">The background colors chosen per template, keyed by template id and then by garment color or "default".</param>
/// <param name="TemplatePlacements">Where this job puts the design on each template's main print area, keyed by template id.</param>
public sealed record BatchMockupSelectionResponseDto(
    Guid BatchJobId,
    IReadOnlyList<Guid> TemplateIds,
    IReadOnlyList<string> GarmentColors,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>>? TemplateColors = null,
    IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>>? TemplateBackgrounds = null,
    IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, MockupAreaSourceDto>>? AreaSources = null,
    IReadOnlyDictionary<Guid, MockupTemplatePlacementDto>? TemplatePlacements = null);
