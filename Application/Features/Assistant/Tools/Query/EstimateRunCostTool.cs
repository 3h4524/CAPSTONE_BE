using APCS.Application.Features.Workflows;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed record EstimateRunCostArgs(string? WorkflowId);

public sealed class EstimateRunCostTool(IRunCostEstimateService estimates) : AssistantTool<EstimateRunCostArgs>
{
    public override string Name => "estimate_run_cost";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description =>
        "Estimate what one Run of a saved workflow will cost, item by item, in USD. Read only: it never starts or pays for anything. The user still confirms the exact cost in the app when the run starts.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"workflowId":{"type":"string","description":"Saved workflow id (GUID). Optional when the open workflow in app_context is meant."}}}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, EstimateRunCostArgs arguments, CancellationToken cancellationToken)
    {
        if (!QueryToolSupport.TryParseId(arguments.WorkflowId ?? context.Client?.WorkflowId?.ToString(), out var workflowId))
            return AssistantToolOutcome.Error("missing_workflow", "Ask the user which workflow they mean.");

        var result = await estimates.EstimateAsync(workflowId, cancellationToken);
        if (result.IsFailure)
            return QueryToolSupport.Failure(result.Error);

        var canvas = context.Client?.Canvas;
        var unsaved = canvas is not null && canvas.WorkflowId == workflowId && canvas.IsDirty;
        return AssistantToolOutcome.Ok(new
        {
            workflowId,
            currency = "USD",
            totalUsd = result.Value.TotalUsd,
            aiVideoBudgetUsd = result.Value.AiVideoBudgetUsd,
            items = result.Value.Items.Select(item => new
            {
                label = item.Label,
                detail = item.Detail,
                quantity = item.Quantity,
                estimatedCostUsd = item.EstimatedCostUsd
            }),
            note = result.Value.Note,
            basedOn = unsaved ? "the saved version; the open canvas has unsaved changes that are not included" : "the saved version"
        });
    }
}
