using System.Threading.Channels;
using APCS.Application.Abstractions.BackgroundJobs;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class AssistantConversationPurgeQueueChannel : IAssistantConversationPurgeQueue
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void RequestPurge() => channel.Writer.TryWrite(true);

    public async ValueTask WaitForRequestAsync(CancellationToken cancellationToken) =>
        await channel.Reader.ReadAsync(cancellationToken);
}
