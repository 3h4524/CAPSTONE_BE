namespace APCS.Application.Features.BatchMockups.Dtos.Response;

/// <summary>
/// Result of compositing mock-ups for every product in a batch job at once (SRS 3.5.9, bulk path).
/// </summary>
/// <param name="GeneratedCount">How many new <c>mockup_images</c> rows were created this call.</param>
/// <param name="NoDesignImageCount">Products in the job with no design image yet (still processing).</param>
/// <param name="NoCompatibleTemplateCount">Products whose type matches none of the selected templates.</param>
/// <param name="NoApprovedImageCount">Products whose designs exist but none is approved yet (jobs that need approval only).</param>
/// <param name="Errors">Per-item failures (e.g. a template with a placeholder base photo), capped.</param>
/// <param name="Images">
/// Every mock-up image tied to this batch job's design images and the currently selected templates —
/// old and newly created together, so the caller can render one complete grid without a second call.
/// </param>
public sealed record GenerateAllMockupsResultDto(
    int GeneratedCount,
    int NoDesignImageCount,
    int NoCompatibleTemplateCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<MockupImageResponseDto> Images,
    int NoApprovedImageCount = 0);
