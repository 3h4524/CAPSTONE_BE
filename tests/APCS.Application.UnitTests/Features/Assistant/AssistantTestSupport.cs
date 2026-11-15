using System.Text.Json;
using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Tools;

namespace APCS.Application.UnitTests.Features.Assistant;

internal static class AssistantTestSupport
{
    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly AssistantCaller Seller = new(true, false);

    public static readonly AssistantCaller Admin = new(false, true);

    public static AssistantChatSession Session(
        string message = "hello",
        AssistantClientContextDto? context = null,
        AssistantCaller? caller = null,
        IReadOnlyList<AssistantHistoryMessage>? history = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), UserId, caller ?? Seller, message, context, history ?? []);

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static AssistantModelUpdate Text(string text) => AssistantModelUpdate.TextDelta(text);

    public static AssistantModelUpdate Done(string text = "", params AssistantToolCall[] calls) =>
        AssistantModelUpdate.Completed(
            new AssistantModelMessage(AssistantMessageRole.Assistant, text, calls),
            10,
            5,
            "test-model");

    public static AssistantToolCall Call(string name, string arguments = "{}", string? callId = null) =>
        new(callId ?? $"call-{name}", name, Json(arguments));
}

internal sealed class FakeChatModel(params IReadOnlyList<AssistantModelUpdate>[] turns) : IAssistantChatModel
{
    private readonly Queue<IReadOnlyList<AssistantModelUpdate>> queue = new(turns);

    public List<AssistantModelRequest> Requests { get; } = [];

    public string ModelName => "fake-model";

    public async IAsyncEnumerable<AssistantModelUpdate> StreamAsync(
        AssistantModelRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        var turn = queue.Count > 0 ? queue.Dequeue() : [AssistantTestSupport.Done()];
        foreach (var update in turn)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
            await Task.Yield();
        }
    }
}

internal sealed class StubTool(
    string name,
    AssistantToolKind kind = AssistantToolKind.Query,
    Func<AssistantToolContext, JsonElement, Task<AssistantToolOutcome>>? execute = null,
    Func<AssistantToolContext, bool>? available = null) : IAssistantTool
{
    public string Name => name;

    public AssistantToolKind Kind => kind;

    public AssistantToolDeclaration Declaration => new(name, $"{name} tool", AssistantTestSupport.Json("""{"type":"object","properties":{}}"""));

    public List<JsonElement> Calls { get; } = [];

    public bool IsAvailable(AssistantToolContext context) => available?.Invoke(context) ?? true;

    public Task<AssistantToolOutcome> ExecuteAsync(AssistantToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        Calls.Add(arguments);
        return execute?.Invoke(context, arguments) ?? Task.FromResult(AssistantToolOutcome.Ok(new { ok = true }));
    }
}

internal sealed class FakeTurnBuffer : IAssistantTurnBuffer
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, List<AssistantTurnEntry>> streams = [];
    private readonly HashSet<Guid> stopRequests = [];
    private long sequence;

    public List<FakeTurnSignal> AppendWatchers { get; } = [];

    public List<FakeTurnSignal> StopWatchers { get; } = [];

    public Action<AssistantTurnEntry>? OnAppend { get; set; }

    public Exception? AppendFailure { get; set; }

    public IReadOnlyList<AssistantTurnEntry> Entries(Guid turnId)
    {
        lock (gate)
            return streams.TryGetValue(turnId, out var entries) ? entries.ToArray() : [];
    }

    public void Expire(Guid turnId)
    {
        lock (gate)
            streams.Remove(turnId);
    }

    public Task<string> AppendAsync(Guid turnId, string type, string data, CancellationToken cancellationToken = default)
    {
        if (AppendFailure is not null)
            throw AppendFailure;
        AssistantTurnEntry entry;
        lock (gate)
        {
            entry = new AssistantTurnEntry($"{++sequence}-0", type, data);
            if (!streams.TryGetValue(turnId, out var entries))
                streams[turnId] = entries = [];
            entries.Add(entry);
        }

        Wake(AppendWatchers);
        OnAppend?.Invoke(entry);
        return Task.FromResult(entry.Id);
    }

    public Task<IReadOnlyList<AssistantTurnEntry>> ReadAfterAsync(Guid turnId, string? afterId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var after = afterId is null ? 0 : long.Parse(afterId.Split('-')[0]);
        lock (gate)
        {
            IReadOnlyList<AssistantTurnEntry> entries = streams.TryGetValue(turnId, out var list)
                ? list.Where(entry => long.Parse(entry.Id.Split('-')[0]) > after).ToArray()
                : [];
            return Task.FromResult(entries);
        }
    }

    public Task<bool> ExistsAsync(Guid turnId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return Task.FromResult(streams.ContainsKey(turnId));
    }

    public Task<IAssistantTurnSignal> WatchAppendsAsync(Guid turnId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAssistantTurnSignal>(Watch(AppendWatchers));

    public Task RequestStopAsync(Guid turnId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            stopRequests.Add(turnId);
        Wake(StopWatchers);
        return Task.CompletedTask;
    }

    public Task<bool> IsStopRequestedAsync(Guid turnId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return Task.FromResult(stopRequests.Contains(turnId));
    }

    public Task<IAssistantTurnSignal> WatchStopAsync(Guid turnId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAssistantTurnSignal>(Watch(StopWatchers));

    private FakeTurnSignal Watch(List<FakeTurnSignal> watchers)
    {
        var signal = new FakeTurnSignal();
        lock (gate)
            watchers.Add(signal);
        return signal;
    }

    private void Wake(List<FakeTurnSignal> watchers)
    {
        FakeTurnSignal[] current;
        lock (gate)
            current = watchers.ToArray();
        foreach (var signal in current)
            signal.Wake();
    }
}

internal sealed class FakeTurnSignal : IAssistantTurnSignal
{
    private readonly SemaphoreSlim wake = new(0, 1);

    public bool Disposed { get; private set; }

    public int Waits { get; private set; }

    public void Wake()
    {
        try
        {
            wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Waits++;
        await wake.WaitAsync(timeout, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
