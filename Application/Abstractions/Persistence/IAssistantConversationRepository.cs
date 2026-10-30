using APCS.Domain.Entities;

namespace APCS.Application.Abstractions.Persistence;

public interface IAssistantConversationRepository : IRepository<AssistantConversation>
{
    Task<int> SoftDeleteAsync(Guid userId, Guid? conversationId, Guid? exceptConversationId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<int> PurgeDeletedAsync(CancellationToken cancellationToken = default);
}
