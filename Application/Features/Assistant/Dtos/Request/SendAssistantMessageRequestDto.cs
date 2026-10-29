using System.Text.Json;

namespace APCS.Application.Features.Assistant.Dtos.Request;

public sealed record SendAssistantMessageRequestDto(Guid? ConversationId, string Message, AssistantClientContextDto? Context);

public sealed record AssistantClientContextDto(
    string Route,
    Guid? WorkflowId,
    Guid? RunId,
    IReadOnlyList<string>? SelectedNodeIds,
    AssistantCanvasDto? Canvas);

public sealed record AssistantCanvasDto(
    Guid WorkflowId,
    long Revision,
    bool IsDirty,
    IReadOnlyList<AssistantCanvasNodeDto> Nodes,
    IReadOnlyList<AssistantCanvasEdgeDto> Edges);

public sealed record AssistantCanvasNodeDto(string Id, string Type, string Label, JsonElement Config);

public sealed record AssistantCanvasEdgeDto(string Id, string Source, string Target);
