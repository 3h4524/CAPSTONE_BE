namespace APCS.Application.Abstractions.Caching;

public interface IAssistantTurnBuffer
{
    Task<string> AppendAsync(Guid turnId, string type, string data, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssistantTurnEntry>> ReadAfterAsync(Guid turnId, string? afterId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid turnId, CancellationToken cancellationToken = default);

    Task<IAssistantTurnSignal> WatchAppendsAsync(Guid turnId, CancellationToken cancellationToken = default);

    Task RequestStopAsync(Guid turnId, CancellationToken cancellationToken = default);

    Task<bool> IsStopRequestedAsync(Guid turnId, CancellationToken cancellationToken = default);

    Task<IAssistantTurnSignal> WatchStopAsync(Guid turnId, CancellationToken cancellationToken = default);
}

public sealed record AssistantTurnEntry(string Id, string Type, string Data);

public interface IAssistantTurnSignal : IAsyncDisposable
{
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
