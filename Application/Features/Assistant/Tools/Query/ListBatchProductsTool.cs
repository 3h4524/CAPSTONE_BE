using APCS.Application.Features.Batches;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed record BatchIdArgs(string? BatchId);

public sealed class ListBatchProductsTool(IBatchService batches) : AssistantTool<BatchIdArgs>
{
    private const int MaxItems = 50;
    private const int MaxKeywords = 10;

    public override string Name => "list_batch_products";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description => "List products in one batch.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"batchId":{"type":"string","description":"Batch id (GUID)."}},"required":["batchId"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, BatchIdArgs arguments, CancellationToken cancellationToken)
    {
        if (!QueryToolSupport.TryParseId(arguments.BatchId, out var batchId))
            return AssistantToolOutcome.Error("invalid_arguments", "batchId must be a valid GUID.");

        var result = await batches.ListProductsAsync(batchId, cancellationToken);
        if (result.IsFailure)
            return QueryToolSupport.Failure(result.Error);

        return AssistantToolOutcome.Ok(new
        {
            products = result.Value.Take(MaxItems).Select(product => new
            {
                id = product.Id,
                name = product.Name,
                productType = product.ProductType,
                niche = product.Niche,
                status = product.Status,
                keywords = product.Keywords.Take(MaxKeywords)
            })
        });
    }
}
