using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Workflows;

namespace APCS.Application.Features.Assistant.Tools.ClientActions;

public sealed record EditWorkflowCanvasArgs(IReadOnlyList<CanvasOperationArgs>? Operations, string? Reply);

public sealed class EditWorkflowCanvasTool(
    WorkflowCanvasPatchSimulator simulator,
    WorkflowCapabilityRegistry capabilities,
    WorkflowGraphValidator graphValidator) : AssistantTool<EditWorkflowCanvasArgs>
{
    public const string ActionKind = "edit_workflow_canvas";

    public override string Name => "edit_workflow_canvas";

    public override AssistantToolKind Kind => AssistantToolKind.ClientAction;

    protected override string Description =>
        "Propose changes to the workflow canvas the user has open. Changes are applied only when the user clicks Apply, and must be saved by the user. Use ref ids for new nodes and reuse them in later operations. Add nodes before connecting them.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"operations":{"type":"array","description":"Ordered canvas operations, at most 20.","items":{"type":"object","properties":{
        "op":{"type":"string","enum":["add_node","remove_node","connect","disconnect","update_config","rename_node"]},
        "ref":{"type":"string","description":"New node id (add_node only), reused later as source, target, after or nodeId."},
        "type":{"type":"string","enum":["product-input","prompt-synthesis","design-image","design-approval","apply-mockup","approval-gate","generate-video","review-video","export-zip","generate-listing"]},
        "label":{"type":"string"},
        "after":{"type":"string","description":"Existing node id to place the new node after."},
        "nodeId":{"type":"string"},
        "source":{"type":"string"},
        "target":{"type":"string"},
        "configJson":{"type":"string","description":"JSON object text with the config keys to set."}},
        "required":["op"]}},"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying what you propose and that they can review and apply it."}},"required":["operations","reply"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) =>
        context.Caller.IsSeller && context.Client?.Canvas is not null;

    protected override Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, EditWorkflowCanvasArgs arguments, CancellationToken cancellationToken)
    {
        var canvas = context.Client!.Canvas!;
        var allowedTypes = capabilities.Get().Nodes.Where(node => node.Enabled).Select(node => node.Type).ToHashSet();
        var simulation = simulator.Simulate(canvas, arguments.Operations ?? [], allowedTypes);
        if (simulation.Errors.Count > 0)
            return Task.FromResult(AssistantToolOutcome.Error("invalid_operations", string.Join("; ", simulation.Errors)));

        var warnings = new List<string>();
        var validation = graphValidator.Validate(simulation.Result);
        if (validation is not null)
            warnings.Add(validation.Message);

        var action = new AssistantClientActionData(
            Guid.NewGuid(),
            ActionKind,
            AssistantJson.SerializeToElement(new { workflowId = canvas.WorkflowId, operations = simulation.Operations, warnings }));
        return Task.FromResult(AssistantToolOutcome.Ok(
            new
            {
                status = "proposed",
                operationCount = simulation.Operations.Count,
                warnings,
                note = "Tell the user to review and click Apply to canvas, then Save."
            },
            true,
            AssistantStreamEvent.ClientAction(action)).WithFinalText(arguments.Reply));
    }
}
