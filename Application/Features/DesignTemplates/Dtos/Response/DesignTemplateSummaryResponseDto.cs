namespace APCS.Application.Features.DesignTemplates.Dtos.Response;

/// <summary>Represents a design-template card in the Seller library.</summary>
public sealed record DesignTemplateSummaryResponseDto(
    Guid Id,
    string Name,
    string? NicheCategory,
    string? ArtStyle,
    string? StyleDescription,
    string? PreviewImageUrl,
    bool IsSystemTemplate,
    int UsageCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool CanEdit,
    bool CanDelete,
    bool CanClone);
