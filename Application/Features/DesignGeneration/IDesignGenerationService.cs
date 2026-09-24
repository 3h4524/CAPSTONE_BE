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
}
