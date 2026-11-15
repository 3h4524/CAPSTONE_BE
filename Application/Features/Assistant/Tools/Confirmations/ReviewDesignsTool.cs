using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.DesignGeneration;
using APCS.Application.Features.DesignGeneration.Dtos.Request;
using APCS.Application.Features.DesignGeneration.Dtos.Response;
using APCS.Common.Constants;
using APCS.Common.Models;

namespace APCS.Application.Features.Assistant.Tools.Confirmations;

public sealed record ReviewDesignsArgs(string? WorkflowId, string? Decision, IReadOnlyList<string>? Products, string? Reply);

public sealed record ReviewDesignsStoredArgs(Guid WorkflowId, Guid BatchJobId, IReadOnlyList<Guid>? DesignImageIds, string Status);

public sealed class ReviewDesignsTool(
    IAssistantRunProgressReader progressReader,
    IDesignGenerationService designJobs,
    IAssistantPendingActionRepository pendingActions,
    TimeProvider timeProvider) : ConfirmableAssistantTool<ReviewDesignsArgs>(pendingActions, timeProvider)
{
    private const int MaxNamesInSummary = 5;

    public override string Name => "review_designs";

    protected override string Description =>
        "Prepare to approve or reject the generated designs that wait for review in the latest run of a workflow, for every waiting product or only the named ones. Approving keeps the first design of each product. The user must confirm.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"workflowId":{"type":"string","description":"Workflow id (GUID). Optional when the open workflow in app_context is meant."},"decision":{"type":"string","enum":["approve","reject"]},"products":{"type":"array","items":{"type":"string"},"description":"Product names to review. Leave out for every product that waits for review."},"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying the review is ready and they must press Confirm."}},"required":["decision","reply"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override string? ReplyOf(ReviewDesignsArgs arguments) => arguments.Reply;

    protected override async Task<Result<AssistantPendingActionDraft>> PreflightAsync(
        AssistantToolContext context,
        ReviewDesignsArgs arguments,
        CancellationToken cancellationToken)
    {
        if (arguments.Decision is not ("approve" or "reject"))
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation("decision must be approve or reject."));
        if (!Guid.TryParse(arguments.WorkflowId ?? context.Client?.WorkflowId?.ToString(), out var workflowId))
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation("workflowId must be a valid GUID."));

        var progress = await progressReader.ReadAsync(workflowId, cancellationToken);
        if (progress.IsFailure)
            return Result.Failure<AssistantPendingActionDraft>(progress.Error);
        if (progress.Value.Stage != AssistantRunStages.DesignApproval || progress.Value.BatchJobId is not { } batchJobId)
            return Result.Failure<AssistantPendingActionDraft>(Error.Conflict("nothing_to_review", "No designs of this workflow wait for review."));

        var job = await designJobs.GetJobAsync(batchJobId, cancellationToken);
        if (job.IsFailure)
            return Result.Failure<AssistantPendingActionDraft>(job.Error);

        var waiting = job.Value.Products.Where(product => product.Status == BatchJobProductStatuses.ImageReviewRequired).ToArray();
        var chosen = waiting;
        if (arguments.Products is { Count: > 0 } names)
        {
            chosen = waiting.Where(product => names.Any(name => Matches(product.ProductName, name))).ToArray();
            if (chosen.Length == 0)
            {
                return Result.Failure<AssistantPendingActionDraft>(Error.Validation(
                    $"None of these products waits for review. Waiting: {string.Join(", ", waiting.Select(product => product.ProductName))}."));
            }
        }

        var approve = arguments.Decision == "approve";
        var ids = chosen.Length == waiting.Length ? null : ImageIds(chosen, approve);
        var verb = approve ? "Approve" : "Reject";
        var named = string.Join(", ", chosen.Take(MaxNamesInSummary).Select(product => product.ProductName));
        var more = chosen.Length > MaxNamesInSummary ? $" and {chosen.Length - MaxNamesInSummary} more" : string.Empty;
        var summary = approve
            ? $"Approves the first design of {named}{more}. Continue to the mock-ups afterwards."
            : $"Rejects the waiting designs of {named}{more}.";

        return Result.Success(new AssistantPendingActionDraft(
            $"{verb} designs of {chosen.Length} product{(chosen.Length == 1 ? "" : "s")}",
            summary,
            new ReviewDesignsStoredArgs(workflowId, batchJobId, ids, approve ? "approved" : "rejected")));
    }

    public override async Task<Result<AssistantConfirmedOutcome>> ExecuteConfirmedAsync(
        Guid pendingActionId,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var stored = AssistantJson.Deserialize<ReviewDesignsStoredArgs>(arguments.GetRawText());
        if (stored is null)
            return Result.Failure<AssistantConfirmedOutcome>(AssistantErrors.ToolUnavailable());

        var result = await designJobs.SetImageApprovalAsync(stored.BatchJobId, new SetImageApprovalRequestDto(stored.DesignImageIds, stored.Status), cancellationToken);
        if (result.IsFailure)
            return Result.Failure<AssistantConfirmedOutcome>(result.Error);

        return Result.Success(new AssistantConfirmedOutcome(
            $"Designs updated: {result.Value.Approved} approved, {result.Value.Rejected} rejected, {result.Value.Pending} still to review.",
            AssistantJson.SerializeToElement(new
            {
                workflowId = stored.WorkflowId,
                batchJobId = stored.BatchJobId,
                approved = result.Value.Approved,
                rejected = result.Value.Rejected,
                pending = result.Value.Pending
            })));
    }

    private static bool Matches(string productName, string requested) =>
        productName.Contains(requested.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<Guid> ImageIds(IEnumerable<BatchJobProductResultDto> products, bool approve) =>
        products.SelectMany(product =>
        {
            var pending = product.Images.Where(image => image.ApprovalStatus == "pending").OrderBy(image => image.VariationIndex);
            return approve ? pending.Take(1) : pending;
        }).Select(image => image.Id).ToArray();
}
