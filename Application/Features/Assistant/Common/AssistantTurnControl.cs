namespace APCS.Application.Features.Assistant.Common;

public sealed class AssistantTurnControl : IDisposable
{
    private readonly CancellationTokenSource timeout;
    private readonly CancellationTokenSource linked;
    private readonly CancellationTokenRegistration timeoutRegistration;
    private readonly CancellationTokenRegistration hostRegistration;
    private AssistantTurnEnd? cancelledAs;

    public AssistantTurnControl(TimeSpan limit, TimeProvider timeProvider, CancellationToken hostStopping)
    {
        timeout = new CancellationTokenSource(limit, timeProvider);
        linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, hostStopping);
        timeoutRegistration = timeout.Token.Register(() => Record(AssistantTurnEnd.TimedOut));
        hostRegistration = hostStopping.Register(() => Record(AssistantTurnEnd.Interrupted));
    }

    public CancellationToken Token => linked.Token;

    public AssistantTurnEnd CancelledAs => Volatile.Read(ref cancelledAs) ?? AssistantTurnEnd.Interrupted;

    public void Stop()
    {
        Record(AssistantTurnEnd.Stopped);
        linked.Cancel();
    }

    public void Dispose()
    {
        timeoutRegistration.Dispose();
        hostRegistration.Dispose();
        linked.Dispose();
        timeout.Dispose();
    }

    private void Record(AssistantTurnEnd end) => Interlocked.CompareExchange(ref cancelledAs, end, null);
}
