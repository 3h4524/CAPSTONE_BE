using APCS.Application.Features.DesignTemplates.Common;

namespace APCS.Application.Features.DesignTemplates.Dtos.Request;

/// <summary>Defines the editable fields for a new personal design template.</summary>
public sealed record CreateDesignTemplateRequestDto(
    string Name,
    string NicheCategory,
    string ArtStyle,
    string BasePrompt,
    string? NegativePrompt,
    IReadOnlyList<DesignTemplateExampleDto> Examples) : IDesignTemplateUpsertRequest;
