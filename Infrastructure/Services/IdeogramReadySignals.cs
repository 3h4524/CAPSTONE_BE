using System.Collections.Concurrent;
using APCS.Application.Abstractions.AI;

namespace APCS.Infrastructure.Services;

/// <summary>
/// The Ideogram generations this server is waiting for, and the webhook calls that say one is ready (see
/// <see cref="IImageReadySignals"/>). One per process: a call reaches the server that sent the request, since the
/// webhook URL is this server's own.
/// </summary>
/// <remarks>
/// Only generations being waited for are kept, from the first status check to the last, so a call for any other id
/// (a late repeat, or a made-up one) adds nothing.
/// </remarks>
public sealed class IdeogramReadySignals : IImageReadySignals
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> waiting = new(StringComparer.Ordinal);

    public bool Ready(string generationId) =>
        waiting.TryGetValue(generationId, out var signal) && signal.TrySetResult();

    /// <summary>Starts listening for this generation's call. A call that comes before the first wait is kept for it.</summary>
    internal void Watch(string generationId) => waiting.TryAdd(generationId, NewSignal());

    /// <summary>Stops listening, once the generation is done or given up.</summary>
    internal void Forget(string generationId) => waiting.TryRemove(generationId, out _);

    /// <summary>
    /// Waits until the next status check: the given time, or less when the call came in the meantime (or during the
    /// last check). Waits the whole time for a generation that is not being watched.
    /// </summary>
    internal async Task WaitAsync(string generationId, TimeSpan delay, CancellationToken cancellationToken)
    {
        if (!waiting.TryGetValue(generationId, out var signal))
        {
            await Task.Delay(delay, cancellationToken);
            return;
        }
        try
        {
            await signal.Task.WaitAsync(delay, cancellationToken);
        }
        catch (TimeoutException)
        {
            return;
        }
        // Used up: a later call (Ideogram may repeat one) wakes the following wait, not this one again.
        waiting.TryUpdate(generationId, NewSignal(), signal);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
