using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Infrastructure.Persistence;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// The publish queue as a use case sees it: a step queued inside a transaction reaches
/// <see cref="WorkflowPublishQueueChannel"/> only once that transaction commits. Queued earlier, the worker could read
/// the step before it says "running", skip it, and leave it to the next recovery pass minutes later.
/// </summary>
public sealed class TransactionalWorkflowPublishQueue(WorkflowPublishQueueChannel channel, AppDbContext db,
    AfterCommitInterceptor afterCommit) : IWorkflowPublishQueue
{
    public void Enqueue(Guid nodeRunId, TimeSpan? delay = null) =>
        afterCommit.Defer(db, () => channel.Enqueue(nodeRunId, delay));

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) => channel.DequeueAsync(cancellationToken);
}
