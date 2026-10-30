using APCS.Application.Features.Assistant.Dtos.Request;

namespace APCS.Application.Features.Assistant.Common;

public sealed record AssistantHistoryMessage(string Role, string Content, string? ToolMemory = null, string? ActionMemory = null);

public sealed record AssistantChatSession(
    Guid ConversationId,
    Guid UserMessageId,
    Guid UserId,
    AssistantCaller Caller,
    string Message,
    AssistantClientContextDto? Context,
    IReadOnlyList<AssistantHistoryMessage> History);
