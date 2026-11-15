using APCS.Application.Abstractions.Persistence;

namespace APCS.Application.Features.Workflows;

public interface IRunGroupPlanner
{
    /// <summary>
    /// Plans the oldest queued child run of a run group: writes its listing, plans its storyboard and queues its render.
    /// False when no run is waiting.
    /// </summary>
    /// <param name="actAs">Called with the run's owner before anything runs as the seller (the listing does).</param>
    Task<bool> AdvanceNextAsync(Action<Guid> actAs, CancellationToken ct);

    /// <summary>Marks a queued run that could not be planned as failed, so it is not picked again.</summary>
    Task FailAsync(Guid runId, string message, CancellationToken ct);
}

/// <summary>Thrown when planning a run failed unexpectedly; carries the run so it can be marked failed.</summary>
public sealed class RunPlanningException(Guid runId, Exception inner) : Exception($"Run {runId} could not be planned.", inner)
{
    public Guid RunId { get; } = runId;
}

public sealed class RunGroupPlanner(IWorkflowStateRepository state, IUnitOfWork uow, WorkflowRuntime runtime, TimeProvider time,
    IWorkflowRunNotifier? notifier = null) : IRunGroupPlanner
{
    public async Task<bool> AdvanceNextAsync(Action<Guid> actAs, CancellationToken ct)
    {
        Domain.Entities.WorkflowRun run;
        await using (var tx = await uow.BeginTransactionAsync(ct))
        {
            if (await state.ClaimQueuedGroupRunAsync(ct) is not { } claimed)
            {
                await tx.CommitAsync(ct);
                return false;
            }
            run = claimed;
            actAs(run.UserId);
            run.Status = "running";
            try
            {
                await runtime.AdvanceAsync(run, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new RunPlanningException(run.Id, exception);
            }
            run.Revision++; run.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        if (notifier != null) await notifier.RunChangedAsync(run, ct);
        return true;
    }

    public async Task FailAsync(Guid runId, string message, CancellationToken ct)
    {
        Domain.Entities.WorkflowRun? run;
        await using (var tx = await uow.BeginTransactionAsync(ct))
        {
            run = await state.LockRunAsync(runId, ct);
            if (run is not { Status: "queued" or "running" }) return;
            var now = time.GetUtcNow().UtcDateTime;
            run.Status = "failed"; run.ErrorMessage = message; run.Revision++; run.UpdatedAt = now;
            await uow.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        if (notifier != null) await notifier.RunChangedAsync(run, ct);
    }
}
