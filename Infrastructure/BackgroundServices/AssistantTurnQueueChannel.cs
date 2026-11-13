using System.Threading.Channels;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Assistant.Common;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class AssistantTurnQueueChannel : IAssistantTurnQueue
{
    private readonly Channel<AssistantChatSession> channel = Channel.CreateUnbounded<AssistantChatSession>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(AssistantChatSession session) => channel.Writer.TryWrite(session);

    public ValueTask<AssistantChatSession> DequeueAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAsync(cancellationToken);
}
