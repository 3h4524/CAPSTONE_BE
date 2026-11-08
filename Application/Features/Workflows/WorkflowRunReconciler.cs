using APCS.Application.Abstractions.Persistence;

namespace APCS.Application.Features.Workflows;

public interface IWorkflowRunReconciler
{
    /// <summary>Fails the step of every running run whose render or export job has failed. Returns how many runs it failed.</summary>
    Task<int> FailRunsWithFailedJobsAsync(CancellationToken ct);
}

/// <summary>
/// Catches a run up with a job that failed without the worker saying so.
/// </summary>
/// <remarks>
/// A worker that reports its failure fails the run at once (<see cref="MediaJobService.FailAsync"/>). A worker
/// that stops without a word leaves a lease that expires: the next claim marks the job failed after its last
/// attempt, but the claim cannot reach the run, which would show "running" for ever. Reading a run used to fix
/// that, which made every poll lock the run; this does it in the background instead.
/// </remarks>
public sealed class WorkflowRunReconciler(IWorkflowStateRepository state, IUnitOfWork uow, WorkflowRuntime runtime,
    ISceneVideoNotifier? sceneNotifier = null) : IWorkflowRunReconciler
{
    /// <summary>Runs handled in one pass, so a pass stays short; the rest follow on the next one.</summary>
    public const int MaximumPerPass = 50;

    public async Task<int> FailRunsWithFailedJobsAsync(CancellationToken ct)
    {
        var candidates = (await state.FindFailedJobsOfRunningRunsAsync(MaximumPerPass, ct))
            .GroupBy(job => job.RunId).Select(group => group.First()).ToArray();
        var failed = 0;
        foreach (var job in candidates)
        {
            await using (var tx = await uow.BeginTransactionAsync(ct))
            {
                // The run may have moved on (a retry, a cancel) since it was read.
                var run = await state.LockRunAsync(job.RunId, ct);
                if (run is not { Status: "running" }) continue;
                await runtime.FailAsync(run, job.NodeRunId, job.ErrorMessage ?? "Render failed.", ct);
                await uow.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            failed++;
            if (sceneNotifier != null) await sceneNotifier.ChangedAsync(job.RunId, ct);
        }
        return failed;
    }
}
