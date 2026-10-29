using System.Collections.Concurrent;
using System.Threading.Channels;
using APCS.Application.Abstractions.BackgroundJobs;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class RagIngestionQueueChannel : IRagIngestionQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, byte> waiting = new();

    public void Enqueue(Guid documentId)
    {
        if (waiting.TryAdd(documentId, 0))
            channel.Writer.TryWrite(documentId);
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
    {
        var documentId = await channel.Reader.ReadAsync(cancellationToken);
        waiting.TryRemove(documentId, out _);
        return documentId;
    }
}
