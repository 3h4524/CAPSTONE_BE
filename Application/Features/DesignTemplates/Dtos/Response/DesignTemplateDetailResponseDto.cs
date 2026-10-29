using APCS.Application.Features.DesignTemplates.Common;

namespace APCS.Application.Features.DesignTemplates.Dtos.Response;

/// <summary>Represents complete design-template details.</summary>
public sealed record DesignTemplateDetailResponseDto(
    Guid Id,
    string Name,
    string? NicheCategory,
    string? ArtStyle,
    string BasePrompt,
    string? NegativePrompt,
    IReadOnlyList<DesignTemplateExampleDto> Examples,
    string? StyleDescription,
    string? PreviewImageUrl,
    bool IsSystemTemplate,
    int UsageCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool CanEdit,
    bool CanDelete,
    bool CanClone);
