namespace APCS.Application.Abstractions.BackgroundJobs;

/// <summary>
/// A batch video run to get ready in the background: its model photos are made (each takes seconds at the image
/// provider) and, when Mockup Approval is automatic, its videos are started, as the seller.
/// </summary>
/// <param name="AutoApprove">Mockup Approval is set to approve automatically: the videos start when the photos are made.</param>
public sealed record RunGroupStartRequest(Guid GroupId, Guid UserId, decimal? AiBudgetUsd, bool AutoApprove);

/// <summary>
/// Hands a started run group to the background worker, so pressing Run answers at once instead of waiting for the
/// image provider. The queue lives in the API process: a group still waiting after a restart shows Mockup Approval
/// as in manual mode, and the seller continues from there.
/// </summary>
public interface IRunGroupStartQueue
{
    void Enqueue(RunGroupStartRequest request);

    /// <summary>Whether the group is waiting in the queue or being made ready now.</summary>
    bool IsPending(Guid groupId);

    /// <summary>The worker is done with the group, whatever came of it.</summary>
    void Done(Guid groupId);

    ValueTask<RunGroupStartRequest> DequeueAsync(CancellationToken cancellationToken);
}
