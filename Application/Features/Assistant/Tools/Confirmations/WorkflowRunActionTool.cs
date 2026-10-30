using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;

namespace APCS.Application.Features.Assistant.Tools.Confirmations;

public sealed record WorkflowRunActionArgs(string? RunId, string? Action, string? VideoId, string? Reply);

public sealed record WorkflowRunActionStoredArgs(
    Guid RunId,
    Guid WorkflowId,
    string Action,
    long ExpectedRevision,
    Guid? VideoId,
    long? ReviewRevision);

public sealed class WorkflowRunActionTool(
    IWorkflowService workflows,
    IWorkflowRunService runs,
    IVideoArtifactService videos,
    IAssistantPendingActionRepository pendingActions,
    TimeProvider timeProvider) : ConfirmableAssistantTool<WorkflowRunActionArgs>(pendingActions, timeProvider)
{
    private static readonly HashSet<string> PaidWhenAiShot = ["resume_mockups", "retry"];

    private static readonly Dictionary<string, (string Title, string Summary)> Texts = new()
    {
        ["cancel"] = ("Cancel run", "Stops this run. It cannot be resumed afterwards."),
        ["resume_mockups"] = ("Continue with approved mockups", "Continues the run using the mockups you approved."),
        ["retry"] = ("Retry failed step", "Retries the step that failed."),
        ["retry_listing"] = ("Retry listing content", "Generates the listing content again."),
        ["approve_video"] = ("Approve video", "Approves the selected video so the run can export."),
        ["reject_video"] = ("Reject video", "Rejects the selected video.")
    };

    public override string Name => "workflow_run_action";

    protected override string Description =>
        "Prepare an action on a workflow run (cancel, continue after mockup approval, retry, retry listing, approve or reject the video). The user must confirm.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"runId":{"type":"string","description":"Run id (GUID). Optional when the run in app_context is meant."},"action":{"type":"string","enum":["cancel","resume_mockups","retry","retry_listing","approve_video","reject_video"]},"videoId":{"type":"string","description":"Video id (GUID) for approve_video or reject_video. Defaults to the newest version."},"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying the action is ready and they must press Confirm."}},"required":["action","reply"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override string? ReplyOf(WorkflowRunActionArgs arguments) => arguments.Reply;

    protected override async Task<Result<AssistantPendingActionDraft>> PreflightAsync(
        AssistantToolContext context,
        WorkflowRunActionArgs arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Action is null || !Texts.TryGetValue(arguments.Action, out var text))
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation("action is not supported."));
        if (!Guid.TryParse(arguments.RunId ?? context.Client?.RunId?.ToString(), out var runId))
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation("runId must be a valid GUID."));

        var run = await runs.GetAsync(runId, cancellationToken);
        if (run.IsFailure)
            return Result.Failure<AssistantPendingActionDraft>(run.Error);

        if (PaidWhenAiShot.Contains(arguments.Action) && await UsesAiShotAsync(run.Value.WorkflowId, cancellationToken))
        {
            return Result.Failure<AssistantPendingActionDraft>(new Error(
                "paid_action_requires_ui",
                "This step uses paid AI video generation. Ask the user to continue from the workflow screen where the cost is shown.",
                ErrorType.Validation));
        }

        Guid? videoId = null;
        long? reviewRevision = null;
        var title = text.Title;
        if (arguments.Action is "approve_video" or "reject_video")
        {
            var video = await FindVideoAsync(runId, arguments.VideoId, cancellationToken);
            if (video is null)
                return Result.Failure<AssistantPendingActionDraft>(Error.Conflict("no_video", "This run has no video to review yet."));
            videoId = video.Id;
            reviewRevision = video.ReviewRevision;
            title = $"{title} v{video.Version}";
        }

        return Result.Success(new AssistantPendingActionDraft(
            title,
            text.Summary,
            new WorkflowRunActionStoredArgs(runId, run.Value.WorkflowId, arguments.Action, run.Value.Revision, videoId, reviewRevision)));
    }

    public override async Task<Result<AssistantConfirmedOutcome>> ExecuteConfirmedAsync(
        Guid pendingActionId,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var stored = AssistantJson.Deserialize<WorkflowRunActionStoredArgs>(arguments.GetRawText());
        if (stored is null)
            return Result.Failure<AssistantConfirmedOutcome>(AssistantErrors.ToolUnavailable());

        var result = await runs.ActionAsync(
            stored.RunId,
            new RunActionRequest(stored.ExpectedRevision, stored.Action, stored.VideoId, stored.ReviewRevision),
            cancellationToken);
        if (result.IsFailure)
            return Result.Failure<AssistantConfirmedOutcome>(result.Error);

        return Result.Success(new AssistantConfirmedOutcome(
            $"Run is now {result.Value.Status}.",
            AssistantJson.SerializeToElement(new { runId = stored.RunId, workflowId = stored.WorkflowId, status = result.Value.Status })));
    }

    private async Task<bool> UsesAiShotAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var workflow = await workflows.GetAsync(workflowId, cancellationToken);
        var node = workflow.IsSuccess
            ? workflow.Value.Definition.Nodes.FirstOrDefault(item => item.Type == "generate-video")
            : null;
        if (node is null)
            return false;
        try
        {
            return WorkflowJson.Config<GenerateVideoConfig>(node).Mode == "ai_shot";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<VideoResponse?> FindVideoAsync(Guid runId, string? requestedId, CancellationToken cancellationToken)
    {
        var list = await videos.ListAsync(runId, cancellationToken);
        if (list.IsFailure)
            return null;
        if (Guid.TryParse(requestedId, out var id))
            return list.Value.FirstOrDefault(video => video.Id == id);
        return list.Value.OrderByDescending(video => video.Version).FirstOrDefault();
    }
}
