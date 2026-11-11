using APCS.Application.Abstractions.Persistence;

namespace APCS.Application.Features.Workflows;

public interface IWorkflowRunReconciler
{
    /// <summary>Fails the step of every running run whose render or export job has failed. Returns how many runs it failed.</summary>
    Task<int> FailRunsWithFailedJobsAsync(CancellationToken ct);

    /// <summary>Whether a render or export job touched since <paramref name="updatedSince"/> could still fail.</summary>
    Task<bool> HasMediaWorkInFlightAsync(DateTime updatedSince, CancellationToken ct);
}

/// <summary>
/// Catches a run up with a job that failed without the worker saying so.
/// </summary>
/// <remarks>
/// A worker that reports its failure fails the run at once (<see cref="MediaJobService.FailAsync"/>), and a worker
/// that stops without a word has its expired lease settled together with its run by <see cref="MediaLeaseRecovery"/>.
/// No known path leaves a failed job under a running run any more; this is the safety net for one that does (a bug,
/// a manual change), run by <c>WorkflowRunFailureSweeper</c> only while media work is in flight.
/// </remarks>
public sealed class WorkflowRunReconciler(IWorkflowStateRepository state, IUnitOfWork uow, WorkflowRuntime runtime,
    ISceneVideoNotifier? sceneNotifier = null, IWorkflowRunNotifier? runNotifier = null) : IWorkflowRunReconciler
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
            Domain.Entities.WorkflowRun? run;
            await using (var tx = await uow.BeginTransactionAsync(ct))
            {
                // The run may have moved on (a retry, a cancel) since it was read.
                run = await state.LockRunAsync(job.RunId, ct);
                if (run is not { Status: "running" }) continue;
                await runtime.FailAsync(run, job.NodeRunId, job.ErrorMessage ?? "Render failed.", ct);
                await uow.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            failed++;
            if (sceneNotifier != null) await sceneNotifier.ChangedAsync(job.RunId, ct);
            if (runNotifier != null) await runNotifier.RunChangedAsync(run, ct);
        }
        return failed;
    }

    public Task<bool> HasMediaWorkInFlightAsync(DateTime updatedSince, CancellationToken ct) =>
        state.HasMediaWorkInFlightAsync(updatedSince, ct);
}
