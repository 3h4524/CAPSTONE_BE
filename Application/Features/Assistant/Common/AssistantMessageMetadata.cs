namespace APCS.Application.Features.Assistant.Common;

public sealed record AssistantMessageMetadata(
    IReadOnlyList<AssistantCitationData>? Citations = null,
    IReadOnlyList<AssistantToolCallData>? ToolCalls = null,
    IReadOnlyList<AssistantClientActionData>? ClientActions = null,
    IReadOnlyList<Guid>? PendingActionIds = null,
    AssistantChoicesData? Choices = null,
    AssistantErrorData? Error = null,
    string? Kind = null,
    string? Route = null)
{
    public string ToJson() => AssistantJson.Serialize(this);
}
