using System.Threading.Channels;
using APCS.Application.Abstractions.BackgroundJobs;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>An unbounded, single-process work queue backed by <see cref="Channel{T}"/>.</summary>
/// <remarks>
/// Registered as a singleton. Items are lost if the process restarts before they are processed — an
/// accepted limitation given the scope: a batch job left "processing" without any produced
/// <c>DesignImage</c> rows can be identified and restarted manually.
/// </remarks>
public sealed class DesignGenerationQueueChannel : IDesignGenerationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid batchJobId) => _channel.Writer.TryWrite(batchJobId);

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
