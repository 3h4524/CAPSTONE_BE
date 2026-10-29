using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;

namespace APCS.Application.Features.Assistant.Tools.Confirmations;

public sealed record StartWorkflowRunArgs(string? WorkflowId, string? Reply);

public sealed record StartWorkflowRunStoredArgs(Guid WorkflowId, long ExpectedWorkflowRevision);

public sealed class StartWorkflowRunTool(
    IWorkflowService workflows,
    IWorkflowRunService runs,
    WorkflowGraphValidator graphValidator,
    IAssistantPendingActionRepository pendingActions,
    TimeProvider timeProvider) : ConfirmableAssistantTool<StartWorkflowRunArgs>(pendingActions, timeProvider)
{
    public override string Name => "start_workflow_run";

    protected override string Description =>
        "Prepare to start a run of a saved workflow. The user must confirm. Never say the run started until the user confirms.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"workflowId":{"type":"string","description":"Saved workflow id (GUID). Optional when the open workflow in app_context is meant."},"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying the run is ready and they must press Confirm."}},"required":["reply"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override string? ReplyOf(StartWorkflowRunArgs arguments) => arguments.Reply;

    protected override async Task<Result<AssistantPendingActionDraft>> PreflightAsync(
        AssistantToolContext context,
        StartWorkflowRunArgs arguments,
        CancellationToken cancellationToken)
    {
        var raw = arguments.WorkflowId ?? context.Client?.WorkflowId?.ToString();
        if (!Guid.TryParse(raw, out var workflowId))
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation("workflowId must be a valid GUID."));

        var workflow = await workflows.GetAsync(workflowId, cancellationToken);
        if (workflow.IsFailure)
            return Result.Failure<AssistantPendingActionDraft>(workflow.Error);

        var canvas = context.Client?.Canvas;
        if (canvas is not null && canvas.WorkflowId == workflowId && canvas.IsDirty)
        {
            return Result.Failure<AssistantPendingActionDraft>(
                Error.Conflict("unsaved_changes", "The canvas has unsaved changes. Ask the user to save first."));
        }

        var invalid = graphValidator.Validate(workflow.Value.Definition);
        if (invalid is not null)
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation(invalid.Message));

        return Result.Success(new AssistantPendingActionDraft(
            $"Run workflow \"{workflow.Value.Name}\"",
            $"Starts a new run from revision {workflow.Value.Revision}. Paid AI steps still ask for approval in the workflow screen.",
            new StartWorkflowRunStoredArgs(workflowId, workflow.Value.Revision)));
    }

    public override async Task<Result<AssistantConfirmedOutcome>> ExecuteConfirmedAsync(
        Guid pendingActionId,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var stored = AssistantJson.Deserialize<StartWorkflowRunStoredArgs>(arguments.GetRawText());
        if (stored is null)
            return Result.Failure<AssistantConfirmedOutcome>(AssistantErrors.ToolUnavailable());

        var result = await runs.StartAsync(
            stored.WorkflowId,
            new StartRunRequest(stored.ExpectedWorkflowRevision, $"assistant:{pendingActionId}"),
            cancellationToken);
        if (result.IsFailure)
            return Result.Failure<AssistantConfirmedOutcome>(result.Error);

        return Result.Success(new AssistantConfirmedOutcome(
            $"Run started ({result.Value.Status}).",
            AssistantJson.SerializeToElement(new { workflowId = stored.WorkflowId, runId = result.Value.Id })));
    }
}
