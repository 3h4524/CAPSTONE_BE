using APCS.Common.Models;

namespace APCS.Application.Features.Assistant.Common;

public static class AssistantErrors
{
    public const string ModelNotConfiguredCode = "assistant.model_not_configured";
    public const string ModelUnavailableCode = "assistant.model_unavailable";

    public static Error Unauthenticated() =>
        Error.Unauthorized("assistant.unauthorized", "The request is not authenticated.");

    public static Error ConversationNotFound() =>
        Error.NotFound("assistant.conversation_not_found", "The conversation was not found.");

    public static Error ActionNotFound() =>
        Error.NotFound("assistant.action_not_found", "The requested action was not found.");

    public static Error ActionNotPending(string status) =>
        Error.Conflict("assistant.action_not_pending", $"This action is already {status}.");

    public static Error ActionExpired() =>
        Error.Conflict("assistant.action_expired", "This action expired. Ask the assistant again.");

    public static Error ToolUnavailable() =>
        Error.Failure("assistant.tool_unavailable", "This action is no longer available.");

    public static (string Code, string Message) FromModelFailure(string? failureCode) => failureCode switch
    {
        "not_configured" => (ModelNotConfiguredCode, "The assistant is not configured yet."),
        _ => (ModelUnavailableCode, "The assistant is temporarily unavailable. Please try again.")
    };
}
