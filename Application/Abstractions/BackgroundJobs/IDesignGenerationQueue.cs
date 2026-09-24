namespace APCS.Application.Abstractions.BackgroundJobs;

/// <summary>
/// An in-process hand-off from the HTTP request that starts a design-generation batch job to the
/// background worker that actually calls the AI provider for every product.
/// </summary>
public interface IDesignGenerationQueue
{
    /// <summary>Queues a batch job for background image generation.</summary>
    void Enqueue(Guid batchJobId);

    /// <summary>Waits for the next queued batch job id.</summary>
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
