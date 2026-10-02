namespace APCS.Application.Features.BatchMockups.Dtos.Request;

/// <summary>
/// Generates a mock-up composite for one already-generated design image.
/// </summary>
/// <param name="MockupTemplateId">Which mock-up template's base photo to composite onto.</param>
/// <param name="X">Optional override X (advanced mode). Omit the whole position to use the template's own default print area (quick mode).</param>
/// <param name="Y">Optional override Y.</param>
/// <param name="Width">Optional override width.</param>
/// <param name="Height">Optional override height.</param>
public sealed record GenerateMockupImageRequestDto(
    Guid MockupTemplateId,
    int? X,
    int? Y,
    int? Width,
    int? Height);
