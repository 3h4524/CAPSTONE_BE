using APCS.Common.Models;

namespace APCS.Application.Features.Assistant.Common;

public static class AssistantErrors
{
    public const string ModelNotConfiguredCode = "assistant.model_not_configured";
    public const string ModelUnavailableCode = "assistant.model_unavailable";
    public const string EmptyResponseCode = "assistant.empty_response";
    public const string StoppedCode = "assistant.stopped";
    public const string TimeoutCode = "assistant.timeout";
    public const string InterruptedCode = "assistant.interrupted";
    public const string BlockedCode = "assistant.blocked";
    public const string BlockedMessage = "Podi can't help with that request.";

    public static Error Unauthenticated() =>
        Error.Unauthorized("assistant.unauthorized", "The request is not authenticated.");

    public static Error ConversationNotFound() =>
        Error.NotFound("assistant.conversation_not_found", "The conversation was not found.");

    public static Error MessageNotFound() =>
        Error.NotFound("assistant.message_not_found", "The message or its action was not found.");

    public static Error InvalidActionState() =>
        Error.Validation("The state must be 'applied' or 'dismissed'.");

    public static Error ActionNotFound() =>
        Error.NotFound("assistant.action_not_found", "The requested action was not found.");

    public static Error ActionNotPending(string status) =>
        Error.Conflict("assistant.action_not_pending", $"This action is already {status}.");

    public static Error ActionExpired() =>
        Error.Conflict("assistant.action_expired", "This action expired. Ask Podi again.");

    public static Error TurnInProgress() =>
        Error.Conflict("assistant.turn_in_progress", "Podi is still answering in this chat. Wait for it or stop it first.");

    public static Error NotStreaming() =>
        Error.Conflict("assistant.not_streaming", "This answer has already finished.");

    public static Error StreamNotFound() =>
        Error.NotFound("assistant.stream_not_found", "This answer can no longer be followed. Reload the conversation.");

    public static Error InvalidEventId() =>
        Error.Validation("The last event id is not valid.");

    public static Error ToolUnavailable() =>
        Error.Failure("assistant.tool_unavailable", "This action is no longer available.");

    public static (string Code, string Message) FromModelFailure(string? failureCode) => failureCode switch
    {
        "not_configured" => (ModelNotConfiguredCode, "Podi is not configured yet."),
        "content_blocked" => (BlockedCode, BlockedMessage),
        _ => (ModelUnavailableCode, "Podi is temporarily unavailable. Please try again.")
    };
}
