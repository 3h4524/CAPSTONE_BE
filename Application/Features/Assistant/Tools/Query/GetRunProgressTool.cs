namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed record GetRunProgressArgs(string? WorkflowId);

public sealed class GetRunProgressTool(IAssistantRunProgressReader reader) : AssistantTool<GetRunProgressArgs>
{
    public override string Name => "get_run_progress";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description =>
        "Where the latest run of a workflow is, across all its parts (designs, design approval, mock-up approval, videos, video review, export, publish), and what the user has to do next. Use it for questions such as how the run is going, is it done, or what do I do now.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"workflowId":{"type":"string","description":"Workflow id (GUID). Optional when the open workflow in app_context is meant."}}}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, GetRunProgressArgs arguments, CancellationToken cancellationToken)
    {
        if (!QueryToolSupport.TryParseId(arguments.WorkflowId ?? context.Client?.WorkflowId?.ToString(), out var workflowId))
            return AssistantToolOutcome.Error("missing_workflow", "Ask the user which workflow they mean.");

        var result = await reader.ReadAsync(workflowId, cancellationToken);
        if (result.IsFailure)
            return QueryToolSupport.Failure(result.Error);

        var progress = result.Value;
        return AssistantToolOutcome.Ok(new
        {
            workflowId = progress.WorkflowId,
            workflowName = progress.WorkflowName,
            stage = progress.Stage,
            state = progress.State,
            nextStep = progress.NextStep,
            done = progress.Done,
            total = progress.Total,
            waitingCount = progress.WaitingCount,
            error = progress.Error,
            runId = progress.RunId,
            startedAt = progress.StartedAt,
            targets = progress.Targets
        });
    }
}
