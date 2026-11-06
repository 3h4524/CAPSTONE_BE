using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

public sealed class AssistantPendingActionRepository(AppDbContext dbContext)
    : Repository<AssistantPendingAction>(dbContext), IAssistantPendingActionRepository
{
    private readonly AppDbContext context = dbContext;

    public async Task<bool> TryClaimForExecutionAsync(Guid id, Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await context.AssistantPendingActions
            .Where(action => action.Id == id
                && action.UserId == userId
                && action.Status == AssistantPendingActionStatuses.Pending
                && action.ExpiresAt > nowUtc)
            .ExecuteUpdateAsync(setters => setters.SetProperty(action => action.Status, AssistantPendingActionStatuses.Executing), cancellationToken) == 1;

    public async Task<bool> TryRejectAsync(Guid id, Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await context.AssistantPendingActions
            .Where(action => action.Id == id
                && action.UserId == userId
                && action.Status == AssistantPendingActionStatuses.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(action => action.Status, AssistantPendingActionStatuses.Rejected)
                .SetProperty(action => action.ResolvedAt, nowUtc), cancellationToken) == 1;
}
