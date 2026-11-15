using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;

namespace APCS.Application.Features.Assistant.Tools.Confirmations;

public sealed record ReviewMockupsArgs(string? WorkflowId, string? Decision, IReadOnlyList<string>? Designs, string? Reply);

public sealed record ReviewMockupItem(Guid MockupId, long ExpectedRevision);

public sealed record ReviewMockupsStoredArgs(Guid WorkflowId, bool Approved, IReadOnlyList<ReviewMockupItem> Mockups);

public sealed class ReviewMockupsTool(
    IAssistantRunProgressReader progressReader,
    IWorkflowRunGroupService runGroups,
    IWorkflowRunService runs,
    IMockupAssetService mockups,
    IAssistantPendingActionRepository pendingActions,
    TimeProvider timeProvider) : ConfirmableAssistantTool<ReviewMockupsArgs>(pendingActions, timeProvider)
{
    private const int MaxMockups = 80;
    private const string Pending = "pending";

    private sealed record Scope(Guid ProductId, string? ArtworkGroupKey, string Name);

    public override string Name => "review_mockups";

    protected override string Description =>
        "Prepare to approve or reject the mock-ups that wait for review at Mockup Approval in the latest run of a workflow, for every design or only the named designs or products. Approved mock-ups are used in the videos. The user must confirm.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"workflowId":{"type":"string","description":"Workflow id (GUID). Optional when the open workflow in app_context is meant."},"decision":{"type":"string","enum":["approve","reject"]},"designs":{"type":"array","items":{"type":"string"},"description":"Design or product names to review. Leave out for every design."},"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying the review is ready and they must press Confirm."}},"required":["decision","reply"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override string? ReplyOf(ReviewMockupsArgs arguments) => arguments.Reply;

    protected override async Task<Result<AssistantPendingActionDraft>> PreflightAsync(
        AssistantToolContext context,
        ReviewMockupsArgs arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Decision is not ("approve" or "reject"))
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation("decision must be approve or reject."));
        if (!Guid.TryParse(arguments.WorkflowId ?? context.Client?.WorkflowId?.ToString(), out var workflowId))
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation("workflowId must be a valid GUID."));

        var progress = await progressReader.ReadAsync(workflowId, cancellationToken);
        if (progress.IsFailure)
            return Result.Failure<AssistantPendingActionDraft>(progress.Error);
        if (progress.Value.Stage != AssistantRunStages.MockupApproval || progress.Value.State != AssistantRunStates.WaitingForUser)
            return Result.Failure<AssistantPendingActionDraft>(Error.Conflict("nothing_to_review", "No mock-ups of this workflow wait for review."));

        var scopes = await ScopesAsync(progress.Value.RunGroupId, progress.Value.RunId, cancellationToken);
        if (scopes.IsFailure)
            return Result.Failure<AssistantPendingActionDraft>(scopes.Error);

        var chosen = scopes.Value;
        if (arguments.Designs is { Count: > 0 } names)
        {
            chosen = chosen.Where(scope => names.Any(name => scope.Name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
            if (chosen.Count == 0)
            {
                return Result.Failure<AssistantPendingActionDraft>(Error.Validation(
                    $"None of these designs is in this run. Designs: {string.Join(", ", scopes.Value.Select(scope => scope.Name))}."));
            }
        }

        var items = new List<ReviewMockupItem>();
        foreach (var productGroup in chosen.GroupBy(scope => scope.ProductId))
        {
            var assets = await mockups.ListAsync(productGroup.Key, cancellationToken);
            if (assets.IsFailure)
                return Result.Failure<AssistantPendingActionDraft>(assets.Error);
            var keys = productGroup.Select(scope => scope.ArtworkGroupKey).ToHashSet();
            items.AddRange(assets.Value
                .Where(asset => asset.ApprovalStatus == Pending && (keys.Contains(null) || keys.Contains(asset.ArtworkGroupKey)))
                .Select(asset => new ReviewMockupItem(asset.Id, asset.Revision)));
        }

        if (items.Count == 0)
            return Result.Failure<AssistantPendingActionDraft>(Error.Conflict("nothing_to_review", "Every mock-up of these designs is already reviewed. The user can continue."));
        if (items.Count > MaxMockups)
            items = items.Take(MaxMockups).ToList();

        var approve = arguments.Decision == "approve";
        var count = $"{items.Count} mock-up{(items.Count == 1 ? "" : "s")}";
        return Result.Success(new AssistantPendingActionDraft(
            $"{(approve ? "Approve" : "Reject")} {count}",
            approve
                ? $"Approves the waiting mock-ups of {chosen.Count} design{(chosen.Count == 1 ? "" : "s")} so they can be used in the videos."
                : $"Rejects the waiting mock-ups of {chosen.Count} design{(chosen.Count == 1 ? "" : "s")}.",
            new ReviewMockupsStoredArgs(workflowId, approve, items)));
    }

    public override async Task<Result<AssistantConfirmedOutcome>> ExecuteConfirmedAsync(
        Guid pendingActionId,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var stored = AssistantJson.Deserialize<ReviewMockupsStoredArgs>(arguments.GetRawText());
        if (stored is null)
            return Result.Failure<AssistantConfirmedOutcome>(AssistantErrors.ToolUnavailable());

        var reviewed = 0;
        var changed = 0;
        foreach (var item in stored.Mockups)
        {
            var result = await mockups.ReviewAsync(item.MockupId, new MockupReviewRequest(item.ExpectedRevision, stored.Approved), cancellationToken);
            if (result.IsSuccess)
                reviewed++;
            else if (result.Error.Type == ErrorType.Conflict)
                changed++;
            else
                return Result.Failure<AssistantConfirmedOutcome>(result.Error);
        }

        var verb = stored.Approved ? "approved" : "rejected";
        var text = changed == 0
            ? $"{reviewed} mock-ups {verb}."
            : $"{reviewed} mock-ups {verb}; {changed} changed in the meantime and were left for review.";
        return Result.Success(new AssistantConfirmedOutcome(
            text,
            AssistantJson.SerializeToElement(new { workflowId = stored.WorkflowId, reviewed, changed, approved = stored.Approved })));
    }

    private async Task<Result<IReadOnlyList<Scope>>> ScopesAsync(Guid? groupId, Guid? runId, CancellationToken cancellationToken)
    {
        if (groupId is { } id)
        {
            var group = await runGroups.GetAsync(id, cancellationToken);
            if (group.IsFailure)
                return Result.Failure<IReadOnlyList<Scope>>(group.Error);
            return Result.Success<IReadOnlyList<Scope>>(group.Value.Targets
                .Select(target => new Scope(target.ProductId, target.ArtworkGroupKey, $"{target.DesignLabel} ({target.ProductName})"))
                .ToArray());
        }

        if (runId is { } single)
        {
            var run = await runs.GetAsync(single, cancellationToken);
            if (run.IsFailure)
                return Result.Failure<IReadOnlyList<Scope>>(run.Error);
            return Result.Success<IReadOnlyList<Scope>>([new Scope(run.Value.ProductId, null, "the product of this run")]);
        }

        return Result.Failure<IReadOnlyList<Scope>>(Error.Conflict("nothing_to_review", "No mock-ups of this workflow wait for review."));
    }
}
