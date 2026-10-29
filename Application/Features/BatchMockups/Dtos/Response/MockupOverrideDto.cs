namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>
/// How one mock-up of a batch job is made instead of by the job's shared settings. It is kept with the job, so
/// it is applied again every time the job's mock-ups are made, whatever the shared settings become.
/// </summary>
/// <param name="MockupTemplateId">The template the design is composited on; it must be for the product's type.</param>
/// <param name="GarmentColor">'#RRGGBB' to recolor the garment with, or null for the template's own color.</param>
/// <param name="BackgroundColor">'#RRGGBB' to put the garment on, or null to keep the photo's background.</param>
/// <param name="PrintArea">Where the design goes on the template's photo, or null for the template's own print area.</param>
/// <param name="Removed">True when no mock-up is made here at all.</param>
public sealed record MockupOverrideDto(
    Guid MockupTemplateId,
    string? GarmentColor,
    string? BackgroundColor,
    MockupPrintAreaDto? PrintArea,
    bool Removed = false);

/// <summary>A print area in the template photo's own pixels.</summary>
public sealed record MockupPrintAreaDto(int X, int Y, int Width, int Height);

/// <summary>A mock-up the job's shared settings would make, which was removed for its product alone.</summary>
public sealed record RemovedMockupSlotDto(
    string SlotKey,
    Guid ProductId,
    Guid DesignImageId,
    Guid MockupTemplateId,
    string TemplateName,
    string? GarmentColor);
