using APCS.Application.Features.Batches;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed class ListBatchesTool(IBatchService batches) : AssistantTool<NoArguments>
{
    private const int MaxItems = 30;

    public override string Name => "list_batches";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description => "List the user's product batches.";

    protected override string ParametersSchemaJson => """{"type":"object","properties":{}}""";

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, NoArguments arguments, CancellationToken cancellationToken)
    {
        var result = await batches.ListAsync(cancellationToken);
        if (result.IsFailure)
            return QueryToolSupport.Failure(result.Error);

        return AssistantToolOutcome.Ok(new
        {
            batches = result.Value.Take(MaxItems).Select(batch => new
            {
                id = batch.Id,
                name = batch.Name,
                status = batch.Status,
                productCount = batch.ProductCount,
                defaultNiche = batch.DefaultNiche,
                defaultProductType = batch.DefaultProductType,
                createdAt = batch.CreatedAt
            })
        });
    }
}
