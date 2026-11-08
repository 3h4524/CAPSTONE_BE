using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

public interface IWorkflowStateRepository
{
    Task LockOwnerAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<WorkflowRun?> LockRunAsync(Guid runId, CancellationToken cancellationToken);
    Task<Workflow?> LockWorkflowAsync(Guid workflowId, CancellationToken cancellationToken);
    Task<MockupImage?> LockMockupAsync(Guid mockupId, CancellationToken cancellationToken);
    Task<MediaJob?> LockJobAsync(Guid jobId, CancellationToken cancellationToken);
    Task<MediaJob?> ClaimAsync(DateTime now, Guid token, CancellationToken cancellationToken) =>
        ClaimAsync(now, token, "media", cancellationToken);
    Task<MediaJob?> ClaimAsync(DateTime now, Guid token, string capability, CancellationToken cancellationToken);
    Task<MediaJob?> ClaimMediaAsync(Guid jobId, long queueVersion, DateTime now, Guid token, CancellationToken cancellationToken);
    Task<string> GetMediaClaimStatusAsync(Guid jobId, long queueVersion, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Render and export jobs that ran out of attempts while their run still says it is running, read without
    /// locking or tracking anything.
    /// </summary>
    Task<IReadOnlyList<FailedMediaJob>> FindFailedJobsOfRunningRunsAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>A failed render or export job, with the step it belongs to.</summary>
public sealed record FailedMediaJob(Guid RunId, Guid NodeRunId, string? ErrorMessage);
