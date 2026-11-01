using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

public sealed class AssistantConversationRepository(AppDbContext dbContext)
    : Repository<AssistantConversation>(dbContext), IAssistantConversationRepository
{
    private readonly AppDbContext context = dbContext;

    public async Task<int> SoftDeleteAsync(
        Guid userId,
        Guid? conversationId,
        Guid? exceptConversationId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default) =>
        await context.AssistantConversations
            .Where(item => item.UserId == userId
                && item.DeletedAt == null
                && (conversationId == null || item.Id == conversationId)
                && (exceptConversationId == null || item.Id != exceptConversationId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DeletedAt, nowUtc), cancellationToken);

    public async Task<int> PurgeDeletedAsync(CancellationToken cancellationToken = default) =>
        await context.AssistantConversations
            .Where(item => item.DeletedAt != null)
            .ExecuteDeleteAsync(cancellationToken);
}
