using APCS.Application.Features.Workflows;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed record GetWorkflowRunArgs(string? RunId, string? WorkflowId);

public sealed class GetWorkflowRunTool(IWorkflowRunService runs) : AssistantTool<GetWorkflowRunArgs>
{
    public override string Name => "get_workflow_run";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description =>
        "Get the status of a workflow run, including each step's status, progress and error message. Pass runId, or workflowId for its latest run. If both are omitted the run or workflow from app_context is used.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"runId":{"type":"string","description":"Run id (GUID)."},"workflowId":{"type":"string","description":"Workflow id (GUID); returns its latest run."}}}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, GetWorkflowRunArgs arguments, CancellationToken cancellationToken)
    {
        var runRaw = arguments.RunId;
        var workflowRaw = arguments.WorkflowId;
        if (string.IsNullOrWhiteSpace(runRaw) && string.IsNullOrWhiteSpace(workflowRaw))
        {
            runRaw = context.Client?.RunId?.ToString();
            workflowRaw = context.Client?.WorkflowId?.ToString();
        }

        Workflows.RunResponse? run;
        if (QueryToolSupport.TryParseId(runRaw, out var runId))
        {
            var result = await runs.GetAsync(runId, cancellationToken);
            if (result.IsFailure)
                return QueryToolSupport.Failure(result.Error);
            run = result.Value;
        }
        else if (QueryToolSupport.TryParseId(workflowRaw, out var workflowId))
        {
            var result = await runs.GetLatestAsync(workflowId, cancellationToken);
            if (result.IsFailure)
                return QueryToolSupport.Failure(result.Error);
            run = result.Value;
        }
        else
        {
            return AssistantToolOutcome.Error("missing_run", "Ask the user which workflow or run they mean.");
        }

        return AssistantToolOutcome.Ok(new
        {
            id = run.Id,
            workflowId = run.WorkflowId,
            status = run.Status,
            revision = run.Revision,
            createdAt = run.CreatedAt,
            errorMessage = run.ErrorMessage,
            nodes = run.Nodes.Select(node => new
            {
                nodeId = node.NodeId,
                nodeType = node.NodeType,
                status = node.Status,
                stage = node.Stage,
                progress = node.Progress,
                errorMessage = node.ErrorMessage
            })
        });
    }
}
