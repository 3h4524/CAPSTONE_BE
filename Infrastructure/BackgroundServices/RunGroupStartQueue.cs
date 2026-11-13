using System.Collections.Concurrent;
using System.Threading.Channels;
using APCS.Application.Abstractions.BackgroundJobs;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>An in-process queue of the run groups to get ready (see <see cref="IRunGroupStartQueue"/>).</summary>
public sealed class RunGroupStartQueue : IRunGroupStartQueue
{
    private readonly Channel<RunGroupStartRequest> _channel = Channel.CreateUnbounded<RunGroupStartRequest>();
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    public void Enqueue(RunGroupStartRequest request)
    {
        // A group is made ready once: a second request for it while one is waiting changes nothing.
        if (_pending.TryAdd(request.GroupId, 0)) _channel.Writer.TryWrite(request);
    }

    public bool IsPending(Guid groupId) => _pending.ContainsKey(groupId);

    public void Done(Guid groupId) => _pending.TryRemove(groupId, out _);

    public ValueTask<RunGroupStartRequest> DequeueAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAsync(cancellationToken);
}
