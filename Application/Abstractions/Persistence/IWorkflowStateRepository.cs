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

    /// <summary>
    /// Whether a render or export job is queued or leased and was touched at or after <paramref name="updatedSince"/>:
    /// while one is, a worker could still fail it or let its lease expire.
    /// </summary>
    Task<bool> HasMediaWorkInFlightAsync(DateTime updatedSince, CancellationToken cancellationToken);

    /// <summary>Leased render and export jobs whose lease expired before <paramref name="now"/>, oldest first, read without locking.</summary>
    Task<IReadOnlyList<ExpiredMediaLease>> FindExpiredMediaLeasesAsync(DateTime now, int limit, CancellationToken cancellationToken);

    Task<WorkflowRunGroup?> LockRunGroupAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>
    /// Locks the oldest queued child run of a run group, skipping the ones another planner holds, or returns null.
    /// </summary>
    Task<WorkflowRun?> ClaimQueuedGroupRunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Renews a worker's lease and records its progress in one statement, locking only the job: the lease token proves
    /// the job is still the worker's, and the run must still be running. The step shown on the canvas is written only
    /// when the stage changes or the progress moves by <paramref name="stepThreshold"/> or more. Null when the lease is
    /// no longer valid.
    /// </summary>
    Task<LeaseRenewal?> RenewLeaseAsync(Guid jobId, Guid leaseToken, string stage, int progress, DateTime now, DateTime leaseUntil,
        int stepThreshold, CancellationToken cancellationToken);
}

/// <param name="StepWritten">Whether the run's step changed, so pages following the run should hear about it.</param>
public sealed record LeaseRenewal(Guid RunId, Guid? RunGroupId, string Stage, int Progress, bool StepWritten);

/// <summary>A failed render or export job, with the step it belongs to.</summary>
public sealed record FailedMediaJob(Guid RunId, Guid NodeRunId, string? ErrorMessage);

/// <summary>A render or export job whose worker stopped renewing its lease.</summary>
public sealed record ExpiredMediaLease(Guid JobId, Guid RunId);
