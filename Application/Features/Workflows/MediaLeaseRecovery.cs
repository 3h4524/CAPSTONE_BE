using APCS.Application.Abstractions.Persistence;

namespace APCS.Application.Features.Workflows;

public interface IMediaLeaseRecovery
{
    /// <summary>
    /// Settles every render or export job whose lease expired before <paramref name="now"/>. Returns how many it settled.
    /// </summary>
    Task<int> RecoverExpiredAsync(DateTime now, CancellationToken ct);
}

/// <summary>
/// The one place a job whose worker stopped without a word is settled. Each job is settled in one transaction with
/// its run: a job that ran out of attempts fails its step and run with it, so a run never says "running" over a
/// failed job.
/// </summary>
/// <remarks>
/// Both queue modes use it: the Streams dispatcher on its recovery pass, and the polling claim before it claims.
/// Before, the polling claim failed exhausted jobs with one SQL update that could not reach their runs, and
/// <see cref="WorkflowRunReconciler"/> had to catch the runs up afterwards.
/// </remarks>
public sealed class MediaLeaseRecovery(IWorkflowStateRepository state, IUnitOfWork uow, WorkflowRuntime runtime,
    ISceneVideoNotifier? sceneNotifier = null) : IMediaLeaseRecovery
{
    /// <summary>Jobs settled in one pass, so a pass stays short; the rest follow on the next one.</summary>
    public const int MaximumPerPass = 100;

    public const string ExhaustedMessage = "Worker lease expired after maximum attempts.";

    public async Task<int> RecoverExpiredAsync(DateTime now, CancellationToken ct)
    {
        var settled = 0;
        // One job per run in a pass: the run stays tracked after its transaction, and a second job of it follows next pass.
        var leases = (await state.FindExpiredMediaLeasesAsync(now, MaximumPerPass, ct)).DistinctBy(lease => lease.RunId).ToArray();
        foreach (var lease in leases)
        {
            var failed = false;
            await using (var tx = await uow.BeginTransactionAsync(ct))
            {
                // The run first, as every other path that changes both locks them.
                var run = await state.LockRunAsync(lease.RunId, ct);
                var job = await state.LockJobAsync(lease.JobId, ct);
                // Another claim, heartbeat or recovery may have settled it since it was read.
                if (job is null || job.Status != "leased" || job.LeaseExpiresAt >= now) continue;

                job.LeaseToken = null; job.LeaseExpiresAt = null; job.UpdatedAt = now;
                if (run is null || run.Status != "running") job.Status = "cancelled";
                else if (job.Attempt >= job.MaximumAttempts)
                {
                    job.Status = "failed";
                    job.ErrorMessage = ExhaustedMessage;
                    await runtime.FailAsync(run, job.WorkflowNodeRunId, ExhaustedMessage, ct);
                    failed = true;
                }
                else { job.Status = "queued"; job.AvailableAt = now; }
                await uow.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            settled++;
            if (failed && sceneNotifier != null) await sceneNotifier.ChangedAsync(lease.RunId, ct);
        }
        return settled;
    }
}
