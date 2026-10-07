namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>A generated mock-up composite (design image overlaid on a product photo).</summary>
public sealed record MockupImageResponseDto(
    Guid Id,
    Guid ProductId,
    Guid DesignImageId,
    Guid MockupTemplateId,
    string MockupImageUrl,
    int MockupWidthPx,
    int MockupHeightPx,
    string ApprovalStatus,
    string? GarmentColor);
