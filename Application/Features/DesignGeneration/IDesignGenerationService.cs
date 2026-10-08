using APCS.Application.Features.DesignGeneration.Dtos.Request;
using APCS.Application.Features.DesignGeneration.Dtos.Response;
using APCS.Common.Models;

namespace APCS.Application.Features.DesignGeneration;

public interface IDesignGenerationService
{
    /// <summary>
    /// Validates BR49/50/51/52/98, synthesizes and persists an <c>AiPrompt</c> per pending product, then
    /// queues the batch job for background generation. Returns as soon as the job is queued — it does
    /// not wait for any image to be generated.
    /// </summary>
    Task<Result<StartGenerationResponseDto>> StartAsync(
        Guid batchJobId,
        StartGenerationRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Actually calls the AI provider for every pending product of the batch job and persists the
    /// results. Called only by <c>DesignGenerationWorker</c> after <see cref="StartAsync"/> queues the
    /// job — never exposed through a controller.
    /// </summary>
    Task ProcessBatchJobAsync(Guid batchJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a batch job (and its still-generating products) as failed after an unexpected error, so
    /// it never stays in "running". Called by the worker from a fresh scope.
    /// </summary>
    Task FailJobAsync(Guid batchJobId, string reason, CancellationToken cancellationToken = default);
    /// <summary>
    /// Re-queues only the failed products of a finished job (SRS 3.4.10 "Retry failed products"). Each
    /// retry consumes image-generation quota again.
    /// </summary>
    Task<Result<StartGenerationResponseDto>> RetryFailedAsync(Guid batchJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks a queued or running job to stop. The product being generated finishes; the ones after it are
    /// failed as cancelled and can be generated again with "Retry failed products".
    /// </summary>
    Task<Result> CancelAsync(Guid batchJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves or rejects generated design images of a finished job (SRS 3.5.12). With no image ids it changes every
    /// image that is still pending. A product counts as approved while at least one of its images is.
    /// </summary>
    Task<Result<ImageApprovalResultDto>> SetImageApprovalAsync(
        Guid batchJobId,
        SetImageApprovalRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a batch job with per-product state and generated images (owner only, BR34).</summary>
    Task<Result<BatchJobDetailDto>> GetJobAsync(Guid batchJobId, CancellationToken cancellationToken = default);

    /// <summary>Lists the jobs of one batch, newest first (owner only, BR34).</summary>
    Task<Result<IReadOnlyList<BatchJobSummaryDto>>> ListJobsForBatchAsync(Guid batchId, CancellationToken cancellationToken = default);
}
