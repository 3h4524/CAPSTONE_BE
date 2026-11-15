using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Batches;
using APCS.Application.Features.Batches.Dtos;
using APCS.Application.Features.Batches.Validators;
using APCS.Common.Models;

namespace APCS.Application.Features.Assistant.Tools.Confirmations;

public sealed record CreateBatchArgs(string? Name, string? Description, string? Niche, string? ProductType, string? Reply);

public sealed class CreateBatchTool(
    IBatchService batches,
    IAssistantPendingActionRepository pendingActions,
    TimeProvider timeProvider) : ConfirmableAssistantTool<CreateBatchArgs>(pendingActions, timeProvider)
{
    public const int MaxNameLength = 255;

    public override string Name => "create_batch";

    protected override string Description =>
        "Prepare to create a new product batch (a group of products a workflow turns into designs). Products are added with add_batch_products after it exists. The user must confirm.";

    protected override string ParametersSchemaJson => Schema;

    private static readonly string Schema = """
        {"type":"object","properties":{"name":{"type":"string"},"description":{"type":"string"},"niche":{"type":"string","description":"Default niche of its products, for example cat lovers."},"productType":{"type":"string","enum":__PRODUCT_TYPES__,"description":"Default product type of its products."},"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying the batch is ready to create and they must press Confirm."}},"required":["name","reply"]}
        """.Replace("__PRODUCT_TYPES__", AssistantJson.Serialize(ProductRules.Types));

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override string? ReplyOf(CreateBatchArgs arguments) => arguments.Reply;

    protected override Task<Result<AssistantPendingActionDraft>> PreflightAsync(
        AssistantToolContext context,
        CreateBatchArgs arguments,
        CancellationToken cancellationToken)
    {
        var name = arguments.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            return Task.FromResult(Result.Failure<AssistantPendingActionDraft>(Error.Validation($"name is required and at most {MaxNameLength} characters.")));
        var type = string.IsNullOrWhiteSpace(arguments.ProductType) ? null : arguments.ProductType.Trim();
        if (type is not null && !ProductRules.Types.Contains(type))
            return Task.FromResult(Result.Failure<AssistantPendingActionDraft>(Error.Validation($"productType must be one of {string.Join(", ", ProductRules.Types)}.")));

        var request = new SaveBatchDto(name, Blank(arguments.Description), Blank(arguments.Niche), type);
        var details = string.Join(", ", new[] { request.DefaultNiche, request.DefaultProductType }.Where(item => item is not null));
        return Task.FromResult(Result.Success(new AssistantPendingActionDraft(
            $"Create batch \"{name}\"",
            details.Length == 0 ? "Creates an empty batch." : $"Creates an empty batch ({details}).",
            request)));
    }

    public override async Task<Result<AssistantConfirmedOutcome>> ExecuteConfirmedAsync(
        Guid pendingActionId,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var stored = AssistantJson.Deserialize<SaveBatchDto>(arguments.GetRawText());
        if (stored is null)
            return Result.Failure<AssistantConfirmedOutcome>(AssistantErrors.ToolUnavailable());

        var created = await batches.CreateAsync(stored, cancellationToken);
        if (created.IsFailure)
            return Result.Failure<AssistantConfirmedOutcome>(created.Error);

        return Result.Success(new AssistantConfirmedOutcome(
            $"Batch \"{created.Value.Name}\" created.",
            AssistantJson.SerializeToElement(new { batchId = created.Value.Id, openPath = "/batches", openLabel = "Open batches" })));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
