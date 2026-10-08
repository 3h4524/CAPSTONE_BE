namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>Applies mock-up templates to a batch.</summary>
/// <param name="TemplateIds">The selected template ids, between 1 and 5 distinct values.</param>
/// <param name="GarmentColors">Up to 5 '#RRGGBB' garment colors shared by every recolorable template that has none of its own.</param>
/// <param name="TemplateColors">
/// Garment colors chosen per template (up to 5 each, '#RRGGBB'), keyed by template id. A recolorable template that
/// appears here is made in these colors only; one that does not falls back to <paramref name="GarmentColors"/>.
/// </param>
public sealed record ApplyMockupTemplatesRequestDto(
    IReadOnlyList<Guid> TemplateIds,
    IReadOnlyList<string>? GarmentColors = null,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>>? TemplateColors = null);
