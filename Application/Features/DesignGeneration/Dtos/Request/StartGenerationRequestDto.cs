namespace APCS.Application.Features.DesignGeneration.Dtos.Request;

/// <summary>Starts image generation for every pending product in a draft batch job.</summary>
/// <param name="DesignTemplateId">Optional: applies this template to every pending product before generating (BR98).</param>
/// <param name="StyleArtPresetId">Optional: applies this style preset to every pending product before generating (BR98).</param>
/// <param name="VariationCount">How many images to generate per product (1-4).</param>
/// <param name="AspectRatio">The requested image aspect ratio, e.g. "1:1", "16:9".</param>
/// <param name="Instructions">Optional wording added to every product's prompt in this job; a product's own custom instructions take precedence.</param>
/// <param name="RequireApproval">
/// <see langword="true"/> holds the finished designs for the Seller's approval before mock-ups are made; <see langword="false"/> approves
/// them automatically. Omit it to keep the behavior from before approval existed.
/// </param>
/// <param name="WorkflowId">Optional: the workflow this job is started from. It is only recorded on the job.</param>
public sealed record StartGenerationRequestDto(
    Guid? DesignTemplateId,
    Guid? StyleArtPresetId,
    int VariationCount,
    string AspectRatio,
    string? Instructions = null,
    bool? RequireApproval = null,
    Guid? WorkflowId = null);
