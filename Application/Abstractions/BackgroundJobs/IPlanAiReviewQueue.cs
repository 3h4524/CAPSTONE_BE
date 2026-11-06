namespace APCS.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Hands an Admin's request for a new plan AI review to the background job, which also refreshes the
/// review on its own schedule, so only one review is ever being written.
/// </summary>
/// <remarks>A review takes minutes, so the request returns at once and the page polls.</remarks>
public interface IPlanAiReviewQueue
{
    /// <summary>Whether a review is being written now, or one an Admin asked for is waiting.</summary>
    bool IsBusy { get; }

    /// <summary>Queues a review for <paramref name="adminId"/>; false when one is already queued or being written.</summary>
    /// <remarks>Marks the queue busy at once, so the page sees it before the job picks the request up.</remarks>
    bool TryRequest(Guid adminId);

    /// <summary>Waits up to <paramref name="timeout"/> for a request; null when none came.</summary>
    Task<Guid?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Marks a review as being written until the returned scope is disposed.</summary>
    IDisposable Generating();
}
