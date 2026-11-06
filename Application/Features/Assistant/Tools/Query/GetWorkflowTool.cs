using APCS.Application.Features.Workflows;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed record WorkflowIdArgs(string? WorkflowId);

public sealed class GetWorkflowTool(IWorkflowService workflows) : AssistantTool<WorkflowIdArgs>
{
    private const int MaxConfigChars = 1500;

    public override string Name => "get_workflow";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description => "Get one saved workflow with its nodes, configuration and connections.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"workflowId":{"type":"string","description":"Workflow id (GUID). Optional when the open workflow in app_context is meant."}}}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, WorkflowIdArgs arguments, CancellationToken cancellationToken)
    {
        var raw = arguments.WorkflowId ?? context.Client?.WorkflowId?.ToString();
        if (!QueryToolSupport.TryParseId(raw, out var id))
            return AssistantToolOutcome.Error("invalid_arguments", "workflowId must be a valid GUID.");

        var result = await workflows.GetAsync(id, cancellationToken);
        if (result.IsFailure)
            return QueryToolSupport.Failure(result.Error);

        var workflow = result.Value;
        return AssistantToolOutcome.Ok(new
        {
            id = workflow.Id,
            name = workflow.Name,
            description = workflow.Description,
            revision = workflow.Revision,
            updatedAt = workflow.UpdatedAt,
            nodes = workflow.Definition.Nodes.Select(node => new
            {
                id = node.Id,
                type = node.Type,
                label = node.Label,
                config = QueryToolSupport.Clip(node.Config.GetRawText(), MaxConfigChars)
            }),
            edges = workflow.Definition.Edges.Select(edge => new { source = edge.Source, target = edge.Target })
        });
    }
}
