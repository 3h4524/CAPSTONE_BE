namespace APCS.Application.Features.DesignGeneration.Dtos.Request;

/// <summary>Generates one product of a finished job again, after the Seller's changes to what it is made from.</summary>
/// <param name="CustomPrompt">The product's own image prompt: null leaves it as it is, empty removes it.</param>
/// <param name="DesignText">The words printed on the design: null leaves them as they are, empty removes them.</param>
/// <param name="VariationCount">How many new designs to make (1-4): null makes as many as the job did.</param>
/// <param name="DesignTemplateId">The template the product's design is made from: null leaves it as it is.</param>
/// <param name="StyleArtPresetId">The art style preset's id: null leaves it as it is, empty removes it.</param>
/// <param name="Instructions">This product's extra instructions in the job: null leaves them, empty means none.</param>
public sealed record RegenerateProductRequestDto(
    string? CustomPrompt = null,
    string? DesignText = null,
    int? VariationCount = null,
    Guid? DesignTemplateId = null,
    string? StyleArtPresetId = null,
    string? Instructions = null);
