using System.Collections.Concurrent;
using System.Threading.Channels;
using APCS.Application.Abstractions.BackgroundJobs;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>An unbounded, single-process work queue backed by <see cref="Channel{T}"/>.</summary>
/// <remarks>
/// Registered as a singleton. Items are lost if the process restarts before they are processed;
/// <see cref="DesignGenerationRecoveryService"/> puts the unfinished jobs back.
/// </remarks>
public sealed class DesignGenerationQueueChannel : IDesignGenerationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    // A job that is already waiting is not added again: unfinished jobs are put back on the queue
    // every minute, and one may wait here for longer than that behind a large job.
    private readonly ConcurrentDictionary<Guid, byte> _waiting = new();

    public void Enqueue(Guid batchJobId)
    {
        if (_waiting.TryAdd(batchJobId, 0))
            _channel.Writer.TryWrite(batchJobId);
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
    {
        var batchJobId = await _channel.Reader.ReadAsync(cancellationToken);
        _waiting.TryRemove(batchJobId, out _);
        return batchJobId;
    }
}
