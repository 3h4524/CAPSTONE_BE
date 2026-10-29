using APCS.Application.Features.BatchMockups;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed record ListMockupTemplatesArgs(string? ProductType);

public sealed class ListMockupTemplatesTool(IMockupTemplateService templates) : AssistantTool<ListMockupTemplatesArgs>
{
    private const int MaxItems = 30;

    public override string Name => "list_mockup_templates";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description =>
        "List mockup templates (id, name, product type) for the Apply mock-up step. Use the returned ids in mockupTemplateIds.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"productType":{"type":"string","description":"Optional product type filter such as tshirt, hoodie, mug, poster, tote_bag, phone_case."}}}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, ListMockupTemplatesArgs arguments, CancellationToken cancellationToken)
    {
        var result = await templates.ListAsync(string.IsNullOrWhiteSpace(arguments.ProductType) ? null : arguments.ProductType, cancellationToken);
        if (result.IsFailure)
            return QueryToolSupport.Failure(result.Error);

        return AssistantToolOutcome.Ok(new
        {
            templates = result.Value.Take(MaxItems).Select(item => new
            {
                id = item.Id,
                name = item.Name,
                productType = item.ProductType,
                isSystem = item.IsSystemTemplate
            })
        });
    }
}
