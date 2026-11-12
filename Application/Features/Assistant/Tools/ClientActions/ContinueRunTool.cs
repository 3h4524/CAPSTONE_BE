using APCS.Application.Features.Assistant.Common;

namespace APCS.Application.Features.Assistant.Tools.ClientActions;

public sealed record ContinueRunArgs(string? Reply);

public sealed class ContinueRunTool(IAssistantRunProgressReader progressReader) : AssistantTool<ContinueRunArgs>
{
    public const string ActionKind = "continue_run";

    private static readonly Dictionary<string, string> Labels = new()
    {
        [AssistantRunStages.DesignApproval] = "Continue to mock-ups",
        [AssistantRunStages.DesignsReady] = "Continue to mock-ups",
        [AssistantRunStages.MockupApproval] = "Continue to the videos"
    };

    public override string Name => "continue_run";

    public override AssistantToolKind Kind => AssistantToolKind.ClientAction;

    protected override string Description =>
        "Offer the Continue button of the run of the open workflow after a review: after design approval it makes the mock-ups (and then the video part), after mock-up approval it starts the videos. It does what Continue on the workflow screen does; paid AI clips still show their cost for the user to confirm. Never say it continued; the user presses the button.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying they can continue from the button."}},"required":["reply"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) =>
        context.Caller.IsSeller && context.Client?.Canvas is not null;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, ContinueRunArgs arguments, CancellationToken cancellationToken)
    {
        var canvas = context.Client!.Canvas!;
        var progress = await progressReader.ReadAsync(canvas.WorkflowId, cancellationToken);
        if (progress.IsFailure)
            return Query.QueryToolSupport.Failure(progress.Error);

        var run = progress.Value;
        if (run.State != AssistantRunStates.WaitingForUser || !Labels.TryGetValue(run.Stage, out var label))
        {
            return AssistantToolOutcome.Error(
                "nothing_to_continue",
                $"The run has nothing to continue right now (stage {run.Stage}, {run.State}). {run.NextStep}");
        }

        var action = new AssistantClientActionData(
            Guid.NewGuid(),
            ActionKind,
            AssistantJson.SerializeToElement(new
            {
                workflowId = canvas.WorkflowId,
                workflowName = canvas.WorkflowName ?? run.WorkflowName,
                stage = run.Stage,
                label,
                batchJobId = run.BatchJobId,
                runGroupId = run.RunGroupId,
                runId = run.RunId
            }));
        return AssistantToolOutcome.Ok(
            new { status = "proposed", stage = run.Stage, note = "The user continues from the button." },
            true,
            AssistantStreamEvent.ClientAction(action)).WithFinalText(arguments.Reply);
    }
}
