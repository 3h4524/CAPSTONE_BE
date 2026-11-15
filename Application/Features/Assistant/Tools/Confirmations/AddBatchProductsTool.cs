using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Batches;
using APCS.Application.Features.Batches.Dtos;
using APCS.Application.Features.Batches.Validators;
using APCS.Common.Models;

namespace APCS.Application.Features.Assistant.Tools.Confirmations;

public sealed record AddBatchProductArgs(string? Name, string? ProductType, string? Niche, IReadOnlyList<string>? Keywords, string? Description, string? DesignText);

public sealed record AddBatchProductsArgs(string? BatchId, IReadOnlyList<AddBatchProductArgs>? Products, string? Reply);

public sealed record AddBatchProductsStoredArgs(Guid BatchId, IReadOnlyList<SaveProductDto> Products);

public sealed class AddBatchProductsTool(
    IBatchService batches,
    IAssistantPendingActionRepository pendingActions,
    TimeProvider timeProvider) : ConfirmableAssistantTool<AddBatchProductsArgs>(pendingActions, timeProvider)
{
    public const int MaxProducts = 20;
    public const int MaxKeywords = 13;

    public override string Name => "add_batch_products";

    protected override string Description =>
        "Prepare to add products to one of the user's batches. Each product needs a name, a product type, a niche and 1 to 13 keywords; write sensible ones from what the user asked when they are not given. The user must confirm.";

    protected override string ParametersSchemaJson => Schema;

    private static readonly string Schema = """
        {"type":"object","properties":{"batchId":{"type":"string","description":"Batch id (GUID) from list_batches or create_batch."},"products":{"type":"array","description":"At most 20 products.","items":{"type":"object","properties":{"name":{"type":"string"},"productType":{"type":"string","enum":__PRODUCT_TYPES__},"niche":{"type":"string"},"keywords":{"type":"array","items":{"type":"string"}},"description":{"type":"string"},"designText":{"type":"string","description":"Words the design prints, if any."}},"required":["name","productType","niche","keywords"]}},"reply":{"type":"string","description":"One short sentence for the user, in the user's language, saying the products are ready to add and they must press Confirm."}},"required":["batchId","products","reply"]}
        """.Replace("__PRODUCT_TYPES__", AssistantJson.Serialize(ProductRules.Types));

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override string? ReplyOf(AddBatchProductsArgs arguments) => arguments.Reply;

    protected override async Task<Result<AssistantPendingActionDraft>> PreflightAsync(
        AssistantToolContext context,
        AddBatchProductsArgs arguments,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(arguments.BatchId, out var batchId))
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation("batchId must be a valid GUID."));
        if (arguments.Products is not { Count: > 0 } requested || requested.Count > MaxProducts)
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation($"Give 1 to {MaxProducts} products."));

        var owned = await batches.ListAsync(cancellationToken);
        if (owned.IsFailure)
            return Result.Failure<AssistantPendingActionDraft>(owned.Error);
        var batch = owned.Value.FirstOrDefault(item => item.Id == batchId);
        if (batch is null)
            return Result.Failure<AssistantPendingActionDraft>(Error.NotFound("BatchNotFound", "Batch not found. Use list_batches."));

        var problems = new List<string>();
        var products = new List<SaveProductDto>();
        foreach (var (item, index) in requested.Select((item, index) => (item, index + 1)))
        {
            var name = item.Name?.Trim();
            var type = item.ProductType?.Trim() ?? batch.DefaultProductType;
            var niche = item.Niche?.Trim() ?? batch.DefaultNiche;
            var keywords = (item.Keywords ?? []).Select(keyword => keyword.Trim()).Where(keyword => keyword.Length > 0).Distinct().Take(MaxKeywords).ToArray();
            if (string.IsNullOrEmpty(name))
                problems.Add($"product {index} has no name");
            else if (type is null || !ProductRules.Types.Contains(type))
                problems.Add($"{name}: productType must be one of {string.Join(", ", ProductRules.Types)}");
            else if (string.IsNullOrWhiteSpace(niche))
                problems.Add($"{name}: niche is required");
            else if (keywords.Length == 0)
                problems.Add($"{name}: at least one keyword is required");
            else
                products.Add(new SaveProductDto(name, type, niche, keywords, Blank(item.Description), null, Blank(item.DesignText)));
        }

        if (problems.Count > 0)
            return Result.Failure<AssistantPendingActionDraft>(Error.Validation(string.Join("; ", problems)));

        var names = string.Join(", ", products.Take(5).Select(product => product.Name));
        var more = products.Count > 5 ? $" and {products.Count - 5} more" : string.Empty;
        return Result.Success(new AssistantPendingActionDraft(
            $"Add {products.Count} product{(products.Count == 1 ? "" : "s")} to \"{batch.Name}\"",
            $"Adds {names}{more}.",
            new AddBatchProductsStoredArgs(batchId, products)));
    }

    public override async Task<Result<AssistantConfirmedOutcome>> ExecuteConfirmedAsync(
        Guid pendingActionId,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var stored = AssistantJson.Deserialize<AddBatchProductsStoredArgs>(arguments.GetRawText());
        if (stored is null)
            return Result.Failure<AssistantConfirmedOutcome>(AssistantErrors.ToolUnavailable());

        var added = 0;
        var failed = new List<string>();
        foreach (var product in stored.Products)
        {
            var result = await batches.AddProductAsync(stored.BatchId, product, cancellationToken);
            if (result.IsSuccess)
                added++;
            else
                failed.Add($"{product.Name} ({result.Error.Message})");
        }

        if (added == 0)
            return Result.Failure<AssistantConfirmedOutcome>(Error.Validation($"No product was added: {string.Join("; ", failed)}"));

        var text = failed.Count == 0 ? $"{added} products added." : $"{added} products added; not added: {string.Join("; ", failed)}.";
        return Result.Success(new AssistantConfirmedOutcome(
            text,
            AssistantJson.SerializeToElement(new { batchId = stored.BatchId, added, failed = failed.Count, openPath = "/batches", openLabel = "Open batches" })));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
