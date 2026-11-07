using System.Threading.Channels;
using APCS.Application.Abstractions.BackgroundJobs;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// A bounded, single-process queue of designs waiting for the OCR check of their text. Each holds its image in memory,
/// so the queue has a cap: past it, a design is not checked rather than piling up memory.
/// </summary>
public sealed class DesignTextCheckQueueChannel : IDesignTextCheckQueue
{
    public const int Capacity = 200;

    private readonly Channel<DesignTextCheckRequest> _channel = Channel.CreateBounded<DesignTextCheckRequest>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public bool TryEnqueue(DesignTextCheckRequest request) => _channel.Writer.TryWrite(request);

    public ValueTask<DesignTextCheckRequest> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
