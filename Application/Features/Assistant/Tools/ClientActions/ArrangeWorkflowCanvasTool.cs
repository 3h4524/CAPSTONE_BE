using APCS.Application.Features.Assistant.Common;

namespace APCS.Application.Features.Assistant.Tools.ClientActions;

public sealed record ArrangeWorkflowCanvasArgs(string? Layout, string? Spacing, int? PerRow, string? Reply);

public sealed class ArrangeWorkflowCanvasTool : AssistantTool<ArrangeWorkflowCanvasArgs>
{
    public const string ActionKind = "arrange_workflow_canvas";
    public const int DefaultPerRow = 3;
    public const int MinPerRow = 2;
    public const int MaxPerRow = 6;

    private static readonly string[] Layouts = ["horizontal", "vertical", "grid"];
    private static readonly string[] Spacings = ["compact", "comfortable", "wide"];

    public override string Name => "arrange_workflow_canvas";

    public override AssistantToolKind Kind => AssistantToolKind.ClientAction;

    protected override string Description =>
        "Tidy the layout of the workflow canvas the user has open by moving the nodes only (nothing is connected or changed). horizontal puts the flow in one left-to-right line, vertical in one top-to-bottom column, grid wraps a long flow into several rows. The user reviews and applies it.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{
        "layout":{"type":"string","enum":["horizontal","vertical","grid"],"description":"horizontal: left to right. vertical: top to bottom. grid: rows that wrap, good for long workflows."},
        "spacing":{"type":"string","enum":["compact","comfortable","wide"],"description":"Gap between nodes. Default comfortable."},
        "perRow":{"type":"integer","description":"Nodes per row for the grid layout, 2 to 6. Default 3."},
        "reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying how you propose to arrange the canvas."}},
        "required":["layout","reply"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) =>
        context.Caller.IsSeller && context.Client?.Canvas is not null;

    protected override Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, ArrangeWorkflowCanvasArgs arguments, CancellationToken cancellationToken)
    {
        var canvas = context.Client!.Canvas!;
        var layout = arguments.Layout?.Trim().ToLowerInvariant();
        if (layout is null || !Layouts.Contains(layout))
            return Task.FromResult(AssistantToolOutcome.Error("invalid_arguments", $"layout must be one of {string.Join(", ", Layouts)}."));

        var spacing = arguments.Spacing?.Trim().ToLowerInvariant() ?? "comfortable";
        if (!Spacings.Contains(spacing))
            return Task.FromResult(AssistantToolOutcome.Error("invalid_arguments", $"spacing must be one of {string.Join(", ", Spacings)}."));

        if (canvas.Nodes.Count < 2)
            return Task.FromResult(AssistantToolOutcome.Error("nothing_to_arrange", "The canvas needs at least two nodes to arrange."));

        var perRow = Math.Clamp(arguments.PerRow ?? DefaultPerRow, MinPerRow, MaxPerRow);
        var action = new AssistantClientActionData(
            Guid.NewGuid(),
            ActionKind,
            AssistantJson.SerializeToElement(new { workflowId = canvas.WorkflowId, layout, spacing, perRow, nodeCount = canvas.Nodes.Count }));
        return Task.FromResult(AssistantToolOutcome.Ok(
            new { status = "proposed", layout, spacing, nodeCount = canvas.Nodes.Count, note = "Tell the user to review and click Apply to canvas." },
            true,
            AssistantStreamEvent.ClientAction(action)).WithFinalText(arguments.Reply));
    }
}
