namespace APCS.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Publish steps of workflow runs waiting to be processed in the background, by node run id. Publishing calls
/// Printify and Etsy for up to minutes, which must not happen inside the run's locked transaction.
/// </summary>
public interface IWorkflowPublishQueue
{
    /// <param name="delay">Wait this long first: Printify is still publishing and is asked again later.</param>
    void Enqueue(Guid nodeRunId, TimeSpan? delay = null);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
