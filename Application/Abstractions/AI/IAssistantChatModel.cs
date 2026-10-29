using System.Text.Json;

namespace APCS.Application.Abstractions.Ai;

public enum AssistantMessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record AssistantToolCall(string CallId, string Name, JsonElement Arguments);

public sealed record AssistantToolResult(string CallId, string Name, string Json);

public sealed record AssistantToolDeclaration(string Name, string Description, JsonElement ParametersSchema);

public sealed record AssistantModelMessage(
    AssistantMessageRole Role,
    string? Text,
    IReadOnlyList<AssistantToolCall> ToolCalls,
    AssistantToolResult? ToolResult = null,
    object? ProviderState = null)
{
    public static AssistantModelMessage System(string text) => new(AssistantMessageRole.System, text, []);

    public static AssistantModelMessage User(string text) => new(AssistantMessageRole.User, text, []);

    public static AssistantModelMessage Assistant(string text) => new(AssistantMessageRole.Assistant, text, []);

    public static AssistantModelMessage Tool(AssistantToolResult result) => new(AssistantMessageRole.Tool, null, [], result);
}

public sealed record AssistantModelRequest(
    IReadOnlyList<AssistantModelMessage> Messages,
    IReadOnlyList<AssistantToolDeclaration> Tools,
    bool AllowTools);

public enum AssistantModelUpdateKind
{
    Text,
    Completed,
    Failed
}

public sealed record AssistantModelUpdate(
    AssistantModelUpdateKind Kind,
    string? Text = null,
    AssistantModelMessage? Message = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    string? Model = null,
    string? FailureCode = null)
{
    public static AssistantModelUpdate TextDelta(string text) => new(AssistantModelUpdateKind.Text, Text: text);

    public static AssistantModelUpdate Completed(AssistantModelMessage message, int? inputTokens, int? outputTokens, string model) =>
        new(AssistantModelUpdateKind.Completed, Message: message, InputTokens: inputTokens, OutputTokens: outputTokens, Model: model);

    public static AssistantModelUpdate Failed(string failureCode) => new(AssistantModelUpdateKind.Failed, FailureCode: failureCode);
}

public interface IAssistantChatModel
{
    string ModelName { get; }

    IAsyncEnumerable<AssistantModelUpdate> StreamAsync(AssistantModelRequest request, CancellationToken cancellationToken = default);
}
