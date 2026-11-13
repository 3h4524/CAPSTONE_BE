using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Batches;
using APCS.Application.Features.Workflows;

namespace APCS.Application.Features.Assistant.Tools.ClientActions;

public sealed record RunWorkflowArgs(string? Reply, string? BlockedReply);

public sealed class RunWorkflowTool(
    WorkflowCanvasPatchSimulator simulator,
    WorkflowGraphValidator graphValidator,
    IBatchService batches) : AssistantTool<RunWorkflowArgs>
{
    public const string ActionKind = "run_workflow";

    public override string Name => "run_workflow";

    public override AssistantToolKind Kind => AssistantToolKind.ClientAction;

    protected override string Description =>
        "Offer a Run button for the workflow the user has open. It does exactly what the Run button of the workflow screen does (design, mock-ups, video, export and publish steps on the canvas), saving unsaved changes first. The app shows the cost and asks the user to confirm it before anything is paid. Never say the run started; the user starts it from the button.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying the run is ready to start from the button and that the app will show the cost to confirm."},"blockedReply":{"type":"string","description":"One short sentence for the user, in the user's language, used instead of reply when the workflow cannot run yet, saying the card lists what to fix first."}},"required":["reply","blockedReply"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) =>
        context.Caller.IsSeller && context.Client?.Canvas is not null;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, RunWorkflowArgs arguments, CancellationToken cancellationToken)
    {
        var canvas = context.Client!.Canvas!;
        var definition = simulator.Snapshot(canvas);
        var issue = graphValidator.Validate(definition, requireProduct: false)?.Message;
        var noPending = issue is null ? await NoPendingProductsAsync(definition, cancellationToken) : null;
        string[] warnings = (issue ?? noPending) is { } warning ? [warning] : [];

        var action = new AssistantClientActionData(
            Guid.NewGuid(),
            ActionKind,
            AssistantJson.SerializeToElement(new { workflowId = canvas.WorkflowId, workflowName = canvas.WorkflowName ?? "This workflow", saveFirst = canvas.IsDirty, warnings }));
        return AssistantToolOutcome.Ok(
            new
            {
                status = "proposed",
                saveFirst = canvas.IsDirty,
                warnings,
                note = warnings.Length == 0
                    ? "The user starts the run from the button. The app asks them to confirm the cost first."
                    : "The open canvas cannot run as it is. Unless a canvas change you proposed in this turn fixes it, tell the user what to fix."
            },
            true,
            AssistantStreamEvent.ClientAction(action)).WithFinalText(warnings.Length == 0 ? arguments.Reply : arguments.BlockedReply);
    }

    private async Task<string?> NoPendingProductsAsync(WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        if (!definition.Nodes.Any(node => WorkflowCapabilityRegistry.BatchSteps.Contains(node.Type)))
            return null;
        var batchId = WorkflowGraphValidator.BatchIdOf(definition);
        if (batchId == Guid.Empty)
            return null;
        var products = await batches.ListProductsAsync(batchId, cancellationToken);
        if (products.IsFailure || products.Value.Any(product => product.Status == PendingProductStatus))
            return null;
        return "This batch has no pending products, so a run has nothing to design. Add products to the batch, or continue the earlier run.";
    }

    private const string PendingProductStatus = "pending";
}
