using APCS.Application.Features.DesignTemplates.Common;

namespace APCS.Application.Features.DesignTemplates.Dtos.Response;

/// <summary>Represents a fully resolved prompt snapshot for a future workflow.</summary>
public sealed record ResolvedDesignTemplatePromptResponseDto(
    Guid DesignTemplateId,
    string PositivePrompt,
    string? NegativePrompt,
    IReadOnlyList<DesignTemplateExampleDto> Examples);
