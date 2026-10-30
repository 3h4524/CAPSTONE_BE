using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

public interface IAssistantPendingActionRepository : IRepository<AssistantPendingAction>
{
    Task<bool> TryClaimForExecutionAsync(Guid id, Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<bool> TryRejectAsync(Guid id, Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default);
}
