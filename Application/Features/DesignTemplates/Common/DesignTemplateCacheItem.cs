namespace APCS.Application.Features.DesignTemplates.Common;

internal sealed record DesignTemplateCacheItem(
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
    DateTimeOffset UpdatedAtUtc);
