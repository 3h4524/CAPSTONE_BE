using System.Text.Json;

namespace APCS.Application.Features.Assistant.Dtos.Response;

public sealed record AssistantConversationSummaryResponseDto(Guid Id, string Title, DateTime UpdatedAt);

public sealed record AssistantMessageResponseDto(Guid Id, string Role, string Content, JsonElement Metadata, DateTime CreatedAt, string Status);

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

public sealed record AssistantRunTargetResponseDto(string Design, string Product, string? Status, string? Step, string? Error);

public sealed record AssistantRunProgressResponseDto(
    Guid WorkflowId,
    string WorkflowName,
    string Stage,
    string State,
    string NextStep,
    int? Done = null,
    int? Total = null,
    int? WaitingCount = null,
    string? Error = null,
    Guid? BatchJobId = null,
    Guid? RunGroupId = null,
    Guid? RunId = null,
    DateTimeOffset? StartedAt = null,
    IReadOnlyList<AssistantRunTargetResponseDto>? Targets = null);
