using APCS.Application.Features.BatchMockups.Dtos.Response;

namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>
/// Generates a mock-up composite for one already-generated design image.
/// </summary>
/// <param name="MockupTemplateId">Which mock-up template's base photo to composite onto.</param>
/// <param name="X">Optional override X (advanced mode). Omit the whole position to use the template's own default print area (quick mode).</param>
/// <param name="Y">Optional override Y.</param>
/// <param name="Width">Optional override width.</param>
/// <param name="Height">Optional override height.</param>
/// <param name="GarmentColor">Optional '#RRGGBB' to recolor the garment with; the template must allow recoloring.</param>
/// <param name="Areas">
/// Optional print areas of the template set for this mock-up: where each goes, how it is turned and what is printed
/// in it. An area not listed keeps the template's settings. Takes the place of X/Y/Width/Height.
/// </param>
public sealed record GenerateMockupImageRequestDto(
    Guid MockupTemplateId,
    int? X,
    int? Y,
    int? Width,
    int? Height,
    string? GarmentColor = null,
    IReadOnlyList<MockupAreaDto>? Areas = null);
