using System.Text.Json;
using APCS.Application.Abstractions.Ai;
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
        new(Guid.NewGuid(), Guid.NewGuid(), UserId, caller ?? Seller, message, context, history ?? []);

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
