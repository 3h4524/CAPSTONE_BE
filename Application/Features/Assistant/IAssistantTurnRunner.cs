using APCS.Application.Features.Assistant.Common;

namespace APCS.Application.Features.Assistant;

public interface IAssistantTurnRunner
{
    Task RunAsync(AssistantChatSession session, CancellationToken stoppingToken);
}
