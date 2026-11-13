using APCS.Application.Features.Assistant.Common;

namespace APCS.Application.Features.Assistant.Tools.ClientActions;

public sealed record SaveWorkflowArgs(string? Reply);

public sealed class SaveWorkflowTool : AssistantTool<SaveWorkflowArgs>
{
    public const string ActionKind = "save_workflow";

    public override string Name => "save_workflow";

    public override AssistantToolKind Kind => AssistantToolKind.ClientAction;

    protected override string Description =>
        "Offer a Save button for the unsaved changes of the workflow the user has open. Use it after the user applied canvas changes and asks to save, or when a later step needs the saved workflow. Not needed before run_workflow, which saves first.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying they can save from the button."}},"required":["reply"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) =>
        context.Caller.IsSeller && context.Client?.Canvas is not null;

    protected override Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, SaveWorkflowArgs arguments, CancellationToken cancellationToken)
    {
        var canvas = context.Client!.Canvas!;
        if (!canvas.IsDirty)
            return Task.FromResult(AssistantToolOutcome.Error("nothing_to_save", "The open workflow has no unsaved changes."));

        var action = new AssistantClientActionData(
            Guid.NewGuid(),
            ActionKind,
            AssistantJson.SerializeToElement(new { workflowId = canvas.WorkflowId, workflowName = canvas.WorkflowName ?? "This workflow" }));
        return Task.FromResult(AssistantToolOutcome.Ok(
            new { status = "proposed", note = "The user saves from the button." },
            true,
            AssistantStreamEvent.ClientAction(action)).WithFinalText(arguments.Reply));
    }
}
