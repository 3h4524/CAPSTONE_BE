namespace APCS.Application.Features.DesignGeneration.Dtos.Request;

/// <summary>Starts image generation for every pending product in a draft batch job.</summary>
/// <param name="DesignTemplateId">Optional: applies this template to every pending product before generating (BR98).</param>
/// <param name="StyleArtPresetId">Optional: applies this style preset to every pending product before generating (BR98).</param>
/// <param name="VariationCount">How many images to generate per product (1-4).</param>
/// <param name="AspectRatio">The requested image aspect ratio, e.g. "1:1", "16:9".</param>
public sealed record StartGenerationRequestDto(
    Guid? DesignTemplateId,
    Guid? StyleArtPresetId,
    int VariationCount,
    string AspectRatio);
