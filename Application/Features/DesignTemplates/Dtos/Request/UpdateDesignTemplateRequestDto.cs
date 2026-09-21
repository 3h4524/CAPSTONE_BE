using APCS.Application.Features.DesignTemplates.Common;

namespace APCS.Application.Features.DesignTemplates.Dtos.Request;

/// <summary>Defines the editable fields for an existing personal design template.</summary>
public sealed record UpdateDesignTemplateRequestDto(
    string Name,
    string NicheCategory,
    string ArtStyle,
    string BasePrompt,
    string? NegativePrompt,
    IReadOnlyList<DesignTemplateExampleDto> Examples) : IDesignTemplateUpsertRequest;
