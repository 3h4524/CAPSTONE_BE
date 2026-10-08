using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.BatchMockups;

/// <summary>Lists mock-up templates and manages per-batch mock-up selection.</summary>
public interface IMockupTemplateService
{
    /// <summary>Lists active templates, optionally filtered by product type.</summary>
    Task<Result<IReadOnlyList<MockupTemplateResponseDto>>> ListAsync(string? productType, CancellationToken cancellationToken = default);

    /// <summary>Reads the mock-up selection stored on a batch.</summary>
    Task<Result<BatchMockupSelectionResponseDto>> GetSelectionAsync(Guid batchJobId, CancellationToken cancellationToken = default);

    /// <summary>Stores the mock-up selection on a draft batch.</summary>
    Task<Result<BatchMockupSelectionResponseDto>> ApplyAsync(Guid batchJobId, ApplyMockupTemplatesRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Creates a personal mock-up template with a real uploaded base photo.</summary>
    Task<Result<MockupTemplateResponseDto>> CreateAsync(CreateMockupTemplateRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Updates a personal mock-up template the current Seller owns.</summary>
    Task<Result<MockupTemplateResponseDto>> UpdateAsync(Guid id, UpdateMockupTemplateRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Deactivates a personal mock-up template the current Seller owns (soft — no hard delete column exists).</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews a base photo before it is saved: its garment mask and whether it can be recolored.
    /// </summary>
    Task<Result<GarmentMaskPreviewResponseDto>> PreviewGarmentMaskAsync(UploadFileDto? photo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Composites a design image onto a mock-up template's base photo (SRS 3.5.9 execution). Uses the
    /// template's stored print area unless the request overrides it (the "advanced" positioning path).
    /// </summary>
    Task<Result<MockupImageResponseDto>> GenerateCompositeAsync(Guid designImageId, GenerateMockupImageRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Composites mock-ups for every product in the batch job at once, using each product's selected,
    /// compatible template(s) and its earliest design image. Idempotent — already-generated pairs are
    /// skipped, so it is safe to call again as more products finish.
    /// </summary>
    Task<Result<GenerateAllMockupsResultDto>> GenerateAllAsync(Guid batchJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same as <see cref="GenerateAllAsync"/> for a job that has just finished generating images, run by
    /// the background worker as the job's owner (there is no signed-in user), so the mock-ups do not wait for
    /// a browser. Fails without changing anything when the job is not finished or has no template selected.
    /// </summary>
    Task<Result<GenerateAllMockupsResultDto>> GenerateAllForJobAsync(Guid batchJobId, CancellationToken cancellationToken = default);
}
