using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>
/// Tells the pages following a run, or the run group it belongs to, that it changed, so they read it again instead
/// of polling. Called after the transaction that changed it has committed.
/// </summary>
public interface IWorkflowRunNotifier
{
    Task RunChangedAsync(WorkflowRun run, CancellationToken cancellationToken = default);

    Task GroupChangedAsync(Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>A render or export moved on to another stage or a noticeably higher progress.</summary>
    Task ProgressAsync(Guid runId, Guid? groupId, string stage, int progress, CancellationToken cancellationToken = default);
}
