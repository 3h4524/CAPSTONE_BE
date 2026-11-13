using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Batches;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;

namespace APCS.Application.Features.Assistant.Tools.Confirmations;

public sealed record CreateWorkflowArgs(string? Name, string? Description, string? BatchId, string? Reply);

public sealed record CreateWorkflowStoredArgs(string Name, string Description, Guid? BatchId);

public sealed class CreateWorkflowTool(
    IWorkflowService workflows,
    IBatchService batches,
    IAssistantPendingActionRepository pendingActions,
    TimeProvider timeProvider) : ConfirmableAssistantTool<CreateWorkflowArgs>(pendingActions, timeProvider)
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 500;

    public override string Name => "create_workflow";

    protected override string Description =>
        "Prepare to create a new workflow that starts with a Product input step, optionally reading the products of a batch. Once it is open, propose its other steps with edit_workflow_canvas. The user must confirm.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"name":{"type":"string","description":"Workflow name, at most 120 characters."},"description":{"type":"string","description":"Optional short description."},"batchId":{"type":"string","description":"Optional batch id (GUID) from list_batches whose products the workflow reads."},"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying the workflow is ready to create and they must press Confirm."}},"required":["name","reply"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override string? ReplyOf(CreateWorkflowArgs arguments) => arguments.Reply;

    protected override async Task<Result<AssistantPendingActionDraft>> PreflightAsync(
        AssistantToolContext context,
        CreateWorkflowArgs arguments,
        CancellationToken cancellationToken)
    {
        var name = arguments.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation($"name is required and at most {MaxNameLength} characters."));

        var description = (arguments.Description ?? string.Empty).Trim();
        if (description.Length > MaxDescriptionLength)
            description = description[..MaxDescriptionLength];

        Guid? batchId = null;
        var batchName = string.Empty;
        if (!string.IsNullOrWhiteSpace(arguments.BatchId))
        {
            if (!Guid.TryParse(arguments.BatchId, out var parsed))
                return Result.Failure<AssistantPendingActionDraft>(Error.Validation("batchId must be a valid GUID."));
            var owned = await batches.ListAsync(cancellationToken);
            if (owned.IsFailure)
                return Result.Failure<AssistantPendingActionDraft>(owned.Error);
            var batch = owned.Value.FirstOrDefault(item => item.Id == parsed);
            if (batch is null)
                return Result.Failure<AssistantPendingActionDraft>(Error.NotFound("BatchNotFound", "Batch not found. Use list_batches."));
            batchId = parsed;
            batchName = batch.Name;
        }

        return Result.Success(new AssistantPendingActionDraft(
            $"Create workflow \"{name}\"",
            batchId is null
                ? "Creates a workflow with a Product input step. Choose its batch and add the other steps afterwards."
                : $"Creates a workflow with a Product input step reading the batch \"{batchName}\". Add the other steps afterwards.",
            new CreateWorkflowStoredArgs(name, description, batchId)));
    }

    public override async Task<Result<AssistantConfirmedOutcome>> ExecuteConfirmedAsync(
        Guid pendingActionId,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var stored = AssistantJson.Deserialize<CreateWorkflowStoredArgs>(arguments.GetRawText());
        if (stored is null)
            return Result.Failure<AssistantConfirmedOutcome>(AssistantErrors.ToolUnavailable());

        var config = AssistantJson.SerializeToElement(new { batchId = stored.BatchId?.ToString() ?? string.Empty, productId = string.Empty });
        var definition = new WorkflowDefinition(2, [new WorkflowNode("input", "product-input", "Product input", new Point(0, 0), config)], []);
        var created = await workflows.SaveAsync(null, new SaveWorkflowRequest(stored.Name, stored.Description, definition), cancellationToken);
        if (created.IsFailure)
            return Result.Failure<AssistantConfirmedOutcome>(created.Error);

        return Result.Success(new AssistantConfirmedOutcome(
            $"Workflow \"{created.Value.Name}\" created.",
            AssistantJson.SerializeToElement(new
            {
                workflowId = created.Value.Id,
                openPath = $"/workflows?id={created.Value.Id}",
                openLabel = "Open workflow"
            })));
    }
}
