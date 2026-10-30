using APCS.Application.Features.Workflows;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed class ListWorkflowsTool(IWorkflowService workflows) : AssistantTool<NoArguments>
{
    private const int MaxItems = 20;

    public override string Name => "list_workflows";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description => "List the user's saved workflows.";

    protected override string ParametersSchemaJson => """{"type":"object","properties":{}}""";

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, NoArguments arguments, CancellationToken cancellationToken)
    {
        var result = await workflows.ListAsync(cancellationToken);
        if (result.IsFailure)
            return QueryToolSupport.Failure(result.Error);

        return AssistantToolOutcome.Ok(new
        {
            workflows = result.Value.Take(MaxItems).Select(item => new
            {
                id = item.Id,
                name = item.Name,
                description = item.Description,
                nodeCount = item.NodeCount,
                revision = item.Revision,
                updatedAt = item.UpdatedAt
            })
        });
    }
}
