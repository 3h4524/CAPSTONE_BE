using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

public interface IWorkflowStateRepository
{
    Task LockOwnerAsync(Guid ownerId, CancellationToken cancellationToken);
    Task<WorkflowRun?> LockRunAsync(Guid runId, CancellationToken cancellationToken);
    Task<Workflow?> LockWorkflowAsync(Guid workflowId, CancellationToken cancellationToken);
    Task<MockupImage?> LockMockupAsync(Guid mockupId, CancellationToken cancellationToken);
    Task<MediaJob?> LockJobAsync(Guid jobId, CancellationToken cancellationToken);
    Task<MediaJob?> ClaimAsync(DateTime now, Guid token, CancellationToken cancellationToken);
}
