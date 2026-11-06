using System.Threading.Channels;
using APCS.Application.Abstractions.BackgroundJobs;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>A single-slot, single-process <see cref="IPlanAiReviewQueue"/> backed by <see cref="Channel{T}"/>.</summary>
/// <remarks>
/// Registered as a singleton. A request is lost if the process restarts before the job takes it;
/// the job's own schedule still writes the next review.
/// </remarks>
public sealed class PlanAiReviewQueueChannel : IPlanAiReviewQueue
{
    private readonly Channel<Guid> requests = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private int generating;

    /// <inheritdoc />
    public bool IsBusy => Volatile.Read(ref generating) == 1;

    /// <inheritdoc />
    public bool TryRequest(Guid adminId)
    {
        if (Interlocked.CompareExchange(ref generating, 1, 0) != 0) return false;
        if (requests.Writer.TryWrite(adminId)) return true;

        Volatile.Write(ref generating, 0);
        return false;
    }

    /// <inheritdoc />
    public async Task<Guid?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(timeout);
        try
        {
            return await requests.Reader.ReadAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public IDisposable Generating()
    {
        Volatile.Write(ref generating, 1);
        return new Scope(this);
    }

    private sealed class Scope(PlanAiReviewQueueChannel queue) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref queue.generating, 0);
    }
}
