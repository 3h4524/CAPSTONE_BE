using System.Text.Json;

namespace APCS.Application.Features.Assistant.Common;

public sealed record AssistantCitationData(Guid DocumentId, string Title, string? SourceUrl);

public sealed record AssistantClientActionData(Guid Id, string Kind, JsonElement Payload);

public sealed record AssistantChoiceOptionData(string Label, string Value);

public sealed record AssistantChoicesData(string? Question, IReadOnlyList<AssistantChoiceOptionData> Options, bool AllowFreeText);

public sealed record AssistantPendingActionEventData(Guid Id, string ToolName, string Title, string Summary, string Status, DateTime ExpiresAt);

public sealed record AssistantErrorData(string Code, string Message);

public sealed record AssistantToolCallData(string Name, bool Ok);

public sealed record AssistantConversationEventData(Guid ConversationId, Guid UserMessageId);

public sealed record AssistantDeltaEventData(string Text);

public sealed record AssistantToolStartedEventData(string CallId, string Name);

public sealed record AssistantToolFinishedEventData(string CallId, string Name, bool Ok);

public sealed record AssistantCitationsEventData(IReadOnlyList<AssistantCitationData> Items);

public sealed record AssistantDoneEventData(Guid AssistantMessageId, string Model, int? InputTokens, int? OutputTokens);

public sealed record AssistantStreamEvent(string Type, object Data)
{
    public static AssistantStreamEvent Conversation(Guid conversationId, Guid userMessageId) =>
        new("conversation", new AssistantConversationEventData(conversationId, userMessageId));

    public static AssistantStreamEvent Delta(string text) => new("delta", new AssistantDeltaEventData(text));

    public static AssistantStreamEvent ToolStarted(string callId, string name) =>
        new("tool_started", new AssistantToolStartedEventData(callId, name));

    public static AssistantStreamEvent ToolFinished(string callId, string name, bool ok) =>
        new("tool_finished", new AssistantToolFinishedEventData(callId, name, ok));

    public static AssistantStreamEvent Citations(IReadOnlyList<AssistantCitationData> items) =>
        new("citations", new AssistantCitationsEventData(items));

    public static AssistantStreamEvent ClientAction(AssistantClientActionData action) => new("client_action", action);

    public static AssistantStreamEvent PendingAction(AssistantPendingActionEventData action) => new("pending_action", action);

    public static AssistantStreamEvent Choices(AssistantChoicesData choices) => new("choices", choices);

    public static AssistantStreamEvent Done(Guid assistantMessageId, string model, int? inputTokens, int? outputTokens) =>
        new("done", new AssistantDoneEventData(assistantMessageId, model, inputTokens, outputTokens));

    public static AssistantStreamEvent Error(string code, string message) => new("error", new AssistantErrorData(code, message));
}
