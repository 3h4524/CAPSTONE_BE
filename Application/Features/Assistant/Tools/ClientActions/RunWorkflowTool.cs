using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Workflows;

namespace APCS.Application.Features.Assistant.Tools.ClientActions;

public sealed record RunWorkflowArgs(string? Reply);

public sealed class RunWorkflowTool(
    WorkflowCanvasPatchSimulator simulator,
    WorkflowGraphValidator graphValidator) : AssistantTool<RunWorkflowArgs>
{
    public const string ActionKind = "run_workflow";

    public override string Name => "run_workflow";

    public override AssistantToolKind Kind => AssistantToolKind.ClientAction;

    protected override string Description =>
        "Offer a Run button for the workflow the user has open. It does exactly what the Run button of the workflow screen does (design, mock-ups, video, export and publish steps on the canvas), saving unsaved changes first. The app shows the cost and asks the user to confirm it before anything is paid. Never say the run started; the user starts it from the button.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying the run is ready to start from the button and that the app will show the cost to confirm."}},"required":["reply"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) =>
        context.Caller.IsSeller && context.Client?.Canvas is not null;

    protected override Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, RunWorkflowArgs arguments, CancellationToken cancellationToken)
    {
        var canvas = context.Client!.Canvas!;
        var issue = graphValidator.Validate(simulator.Snapshot(canvas), requireProduct: false)?.Message;
        string[] warnings = issue is null ? [] : [issue];

        var action = new AssistantClientActionData(
            Guid.NewGuid(),
            ActionKind,
            AssistantJson.SerializeToElement(new { workflowId = canvas.WorkflowId, workflowName = canvas.WorkflowName ?? "This workflow", saveFirst = canvas.IsDirty, warnings }));
        return Task.FromResult(AssistantToolOutcome.Ok(
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
            AssistantStreamEvent.ClientAction(action)).WithFinalText(warnings.Length == 0 ? arguments.Reply : null));
    }
}
