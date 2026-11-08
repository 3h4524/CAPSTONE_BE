using System.Collections.Concurrent;
using System.Threading.Channels;
using APCS.Application.Abstractions.BackgroundJobs;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// In-process queue of publish steps. Items are lost on restart; <see cref="WorkflowPublishWorker"/> finds the steps
/// still running when it starts, and every few minutes, and puts them back.
/// </summary>
public sealed class WorkflowPublishQueueChannel : IWorkflowPublishQueue
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    // A step already waiting (or waiting out a delay) is not added twice.
    private readonly ConcurrentDictionary<Guid, byte> waiting = new();

    public void Enqueue(Guid nodeRunId, TimeSpan? delay = null)
    {
        if (!waiting.TryAdd(nodeRunId, 0))
            return;
        if (delay is not { } wait || wait <= TimeSpan.Zero)
        {
            channel.Writer.TryWrite(nodeRunId);
            return;
        }
        _ = Task.Delay(wait).ContinueWith(_ => channel.Writer.TryWrite(nodeRunId), TaskScheduler.Default);
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
    {
        var nodeRunId = await channel.Reader.ReadAsync(cancellationToken);
        waiting.TryRemove(nodeRunId, out _);
        return nodeRunId;
    }
}
