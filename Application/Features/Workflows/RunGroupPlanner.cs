using APCS.Application.Abstractions.Persistence;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Workflows;

/// <summary>
/// A child run the planner has taken: what it still has to do with no transaction open, each part in a scope of its own
/// (<see cref="IRunGroupPlanner.PreparePromptsAsync"/>, then <see cref="IRunGroupPlanner.WriteListingAsync"/>).
/// </summary>
public sealed record GroupRunClaim(Guid RunId, Guid UserId, bool PreparesPrompts, bool WritesListing);

public interface IRunGroupPlanner
{
    /// <summary>
    /// Plans a queued child run, or starts a pending export after all videos in its group pass review, all in one go.
    /// False when no run is waiting. The same as <see cref="ClaimNextAsync"/> followed by its other parts in this scope.
    /// </summary>
    /// <param name="actAs">Called with the run's owner before anything runs as the seller (the listing does).</param>
    Task<bool> AdvanceNextAsync(Action<Guid> actAs, CancellationToken ct);

    /// <summary>
    /// Takes the next run to plan and does its short part, so it shows as working at once: the AI scene prompts and the
    /// listing, which wait on Gemini, are only marked, for the other parts to do. Null when no run is waiting. Also takes
    /// a run whose other parts stopped half way (the API restarted), and hands them out again.
    /// </summary>
    Task<GroupRunClaim?> ClaimNextAsync(Action<Guid> actAs, CancellationToken ct);

    /// <summary>Writes the run's AI scene prompts with no transaction open, then sends its clips.</summary>
    Task PreparePromptsAsync(GroupRunClaim claim, Action<Guid> actAs, CancellationToken ct);

    /// <summary>Writes the run's listing with no transaction open, then lets the steps that waited for it go on.</summary>
    Task WriteListingAsync(GroupRunClaim claim, Action<Guid> actAs, CancellationToken ct);

    /// <summary>Marks a queued run that could not be planned as failed, so it is not picked again.</summary>
    Task FailAsync(Guid runId, string message, CancellationToken ct);
}

/// <summary>Thrown when planning a run failed unexpectedly; carries the run so it can be marked failed.</summary>
public sealed class RunPlanningException(Guid runId, Exception inner) : Exception($"Run {runId} could not be planned.", inner)
{
    public Guid RunId { get; } = runId;
}

/// <remarks>
/// Writing the prompts of a video's AI scenes asks Gemini to look at each opening frame and rewrite the scene, which
/// takes tens of seconds. Done inside the planner's transaction, the run stayed queued (grey on the canvas) and locked
/// all that time. Now the run is taken and marked first, the slow parts run with nothing locked, and each ends in a
/// short transaction of its own that reads the run afresh, so a run cancelled meanwhile is left as it is.
/// </remarks>
public sealed class RunGroupPlanner(IWorkflowStateRepository state, IUnitOfWork uow, WorkflowRuntime runtime, TimeProvider time,
    IWorkflowRunNotifier? notifier = null, IRepository<WorkflowRun>? runs = null) : IRunGroupPlanner
{
    /// <summary>
    /// A run whose prompts or listing nobody has touched for this long was left half way (the API stopped): it is taken
    /// up again. Well beyond what writing them takes, so a run still being worked on is never taken twice.
    /// </summary>
    public static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(10);

    public async Task<bool> AdvanceNextAsync(Action<Guid> actAs, CancellationToken ct)
    {
        if (await ClaimNextAsync(actAs, ct) is not { } claim) return false;
        if (claim.PreparesPrompts) await PreparePromptsAsync(claim, actAs, ct);
        if (claim.WritesListing) await WriteListingAsync(claim, actAs, ct);
        return true;
    }

    public async Task<GroupRunClaim?> ClaimNextAsync(Action<Guid> actAs, CancellationToken ct)
    {
        WorkflowRun run;
        (bool PreparesPrompts, bool WritesListing) work;
        await using (var tx = await uow.BeginTransactionAsync(ct))
        {
            var claimed = await state.ClaimQueuedGroupRunAsync(ct)
                ?? await state.ClaimStalledGroupRunAsync(time.GetUtcNow().UtcDateTime - StalledAfter,
                    WorkflowRuntime.PreparingPromptsStage, WorkflowRuntime.WritingListingStage, ct);
            if (claimed is null)
            {
                await tx.CommitAsync(ct);
                return null;
            }
            run = claimed;
            actAs(run.UserId);
            // A queued run starts now; one taken up again keeps where it was (running, or waiting for the seller).
            if (run.Status == "queued") run.Status = "running";
            try
            {
                work = await runtime.BeginGroupRunAsync(run, ct);
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
        return new(run.Id, run.UserId, work.PreparesPrompts, work.WritesListing);
    }

    public async Task PreparePromptsAsync(GroupRunClaim claim, Action<Guid> actAs, CancellationToken ct)
    {
        actAs(claim.UserId);
        if (await ReadAsync(claim.RunId, ct) is not { Status: "running" } run) return;
        Result<Storyboard>? plan;
        try
        {
            plan = await runtime.PreparePromptsAsync(run, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RunPlanningException(run.Id, exception);
        }
        if (plan is null) return;
        await FinishAsync(claim.RunId, locked => runtime.FinishPromptsAsync(locked, plan, ct), ct);
    }

    public async Task WriteListingAsync(GroupRunClaim claim, Action<Guid> actAs, CancellationToken ct)
    {
        actAs(claim.UserId);
        // A run that went on to wait for the seller still gets its listing: only a stopped one does not.
        if (await ReadAsync(claim.RunId, ct) is not { Status: "running" or "waiting_for_review" or "waiting_for_input" } run) return;
        var listing = await runtime.WriteListingAsync(run, ct);
        if (listing is null) return;
        await FinishAsync(claim.RunId, locked => runtime.FinishListingAsync(locked, listing, ct), ct,
            statuses: ["running", "waiting_for_review", "waiting_for_input"]);
    }

    /// <summary>
    /// The run as it is now, for the slow parts to read with no transaction open. Not tracked, so the locked copy the
    /// short transaction after it reads is fresh.
    /// </summary>
    private async Task<WorkflowRun?> ReadAsync(Guid runId, CancellationToken ct) =>
        runs is null
            ? await state.LockRunAsync(runId, ct)
            : await runs.Query().SingleOrDefaultAsync(x => x.Id == runId, ct);

    /// <summary>The short transaction that ends a slow part: the run locked and read again, still in one of the statuses.</summary>
    private async Task FinishAsync(Guid runId, Func<WorkflowRun, Task> finish, CancellationToken ct, string[]? statuses = null)
    {
        WorkflowRun? run;
        await using (var tx = await uow.BeginTransactionAsync(ct))
        {
            run = await state.LockRunAsync(runId, ct);
            // Cancelled or failed meanwhile: what was written is simply not used.
            if (run is null || !(statuses ?? ["running"]).Contains(run.Status)) return;
            try
            {
                await finish(run);
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
    }

    public async Task FailAsync(Guid runId, string message, CancellationToken ct)
    {
        WorkflowRun? run;
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
