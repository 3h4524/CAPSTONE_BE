namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>A generated mock-up composite (design image overlaid on a product photo).</summary>
/// <param name="SlotKey">In a batch job's results, which of the job's mock-ups this is; what an override is saved under.</param>
/// <param name="Override">In a batch job's results, how this mock-up is made apart from the job's settings, if it is.</param>
public sealed record MockupImageResponseDto(
    Guid Id,
    Guid ProductId,
    Guid DesignImageId,
    Guid MockupTemplateId,
    string MockupImageUrl,
    int MockupWidthPx,
    int MockupHeightPx,
    string ApprovalStatus,
    string? GarmentColor,
    string? BackgroundColor = null,
    string? SlotKey = null,
    MockupOverrideDto? Override = null);
