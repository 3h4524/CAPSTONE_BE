using APCS.Application.Features.Assistant.Common;

namespace APCS.Application.Abstractions.BackgroundJobs;

public interface IAssistantTurnQueue
{
    void Enqueue(AssistantChatSession session);

    ValueTask<AssistantChatSession> DequeueAsync(CancellationToken cancellationToken);
}
