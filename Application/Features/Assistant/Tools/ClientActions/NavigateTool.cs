using APCS.Application.Features.Assistant.Common;

namespace APCS.Application.Features.Assistant.Tools.ClientActions;

public sealed record NavigateArgs(string? Path, string? Label);

public sealed class NavigateTool : AssistantTool<NavigateArgs>
{
    private const int MaxLabelLength = 40;

    public override string Name => "navigate";

    public override AssistantToolKind Kind => AssistantToolKind.ClientAction;

    protected override string Description =>
        "Offer the user a button that opens a page of the app. Only app pages are allowed. The user decides whether to click it.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"path":{"type":"string","description":"App path such as /usage, /batches or /workflows?id=<workflowId>."},"label":{"type":"string","description":"Short button text, at most 40 characters."}},"required":["path","label"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) => true;

    protected override Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, NavigateArgs arguments, CancellationToken cancellationToken)
    {
        if (!AssistantNavigationRoutes.IsAllowed(arguments.Path, context.Caller.IsSeller, context.Caller.IsAdmin))
            return Task.FromResult(AssistantToolOutcome.Error("route_not_allowed", "Only app pages can be opened."));
        if (string.IsNullOrWhiteSpace(arguments.Label) || arguments.Label.Length > MaxLabelLength)
            return Task.FromResult(AssistantToolOutcome.Error("invalid_arguments", $"label must be 1 to {MaxLabelLength} characters."));

        var action = new AssistantClientActionData(
            Guid.NewGuid(),
            "navigate",
            AssistantJson.SerializeToElement(new { path = arguments.Path, label = arguments.Label }));
        return Task.FromResult(AssistantToolOutcome.Ok(new { status = "offered" }, false, AssistantStreamEvent.ClientAction(action)));
    }
}
