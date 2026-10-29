namespace APCS.Application.Abstractions.BackgroundJobs;

/// <summary>
/// Lets exactly one worker take a batch job when several application instances share a database.
/// </summary>
/// <remarks>
/// The work queue lives in each instance's memory, and an instance that starts puts every unfinished job it
/// finds back on its own queue. So the same job can be waiting in more than one place: the claim, made in the
/// database, decides which worker runs it.
/// </remarks>
public interface IBatchJobClaims
{
    /// <summary>
    /// Marks the job as running for the caller.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the job was queued, or was left running by a worker that has since stopped.
    /// <see langword="false"/> when another worker is on it, or it is finished, not started or deleted.
    /// </returns>
    Task<bool> TryClaimAsync(Guid batchJobId, CancellationToken cancellationToken = default);
}
