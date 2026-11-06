using System.Text.Json;

namespace APCS.Application.Features.Assistant.Dtos.Response;

public sealed record AssistantConversationSummaryResponseDto(Guid Id, string Title, DateTime UpdatedAt);

public sealed record AssistantMessageResponseDto(Guid Id, string Role, string Content, JsonElement Metadata, DateTime CreatedAt);

public sealed record AssistantPendingActionResponseDto(
    Guid Id,
    string ToolName,
    string Title,
    string Summary,
    string Status,
    DateTime ExpiresAt,
    JsonElement? Result,
    string? ErrorMessage);

public sealed record AssistantConversationDetailResponseDto(
    Guid Id,
    string Title,
    IReadOnlyList<AssistantMessageResponseDto> Messages,
    IReadOnlyList<AssistantPendingActionResponseDto> PendingActions);

public sealed record AssistantActionResultResponseDto(
    AssistantPendingActionResponseDto Action,
    AssistantMessageResponseDto Message);
