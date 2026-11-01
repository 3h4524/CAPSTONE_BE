using APCS.Application.Features.Assistant.Catalog;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed record NodeCatalogArgs(IReadOnlyList<string>? Types);

public sealed class GetNodeCatalogTool : AssistantTool<NodeCatalogArgs>
{
    public override string Name => "get_node_catalog";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description =>
        "Get the workflow step types with the settings the assistant can change (names, valid values, limits) and the rules for connecting steps. Call it before editing node settings or connecting steps. Pass types to get the full detail of only those steps.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"types":{"type":"array","description":"Step types to describe in full, for example [\"design-image\",\"generate-listing\"]. Omit for a short overview of all steps.","items":{"type":"string"}}}}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, NodeCatalogArgs arguments, CancellationToken cancellationToken)
    {
        var requested = arguments.Types?.Where(type => !string.IsNullOrWhiteSpace(type)).ToHashSet() ?? [];
        var unknown = requested.Where(type => WorkflowNodeCatalog.Find(type) is null).ToArray();
        if (unknown.Length > 0)
        {
            return Task.FromResult(AssistantToolOutcome.Error(
                "unknown_type",
                $"Unknown step types: {string.Join(", ", unknown)}. Valid types: {string.Join(", ", WorkflowNodeCatalog.Nodes.Select(node => node.Type))}."));
        }

        var detailed = requested.Count > 0;
        var nodes = WorkflowNodeCatalog.Nodes
            .Where(node => !detailed || requested.Contains(node.Type))
            .Select(node => new
            {
                type = node.Type,
                label = node.Label,
                description = node.Description,
                runOrder = node.RunOrder,
                note = node.Note,
                settings = node.Fields.Select(field => new
                {
                    name = field.Name,
                    kind = field.Kind,
                    label = field.Label,
                    options = field.Options is null ? null : detailed ? field.Options.Select(option => new { value = option.Value, label = option.Label }).Cast<object>().ToArray() : field.Options.Select(option => (object)option.Value).ToArray(),
                    min = field.Min,
                    max = field.Max,
                    maxLength = field.MaxLength,
                    optional = field.Optional ? true : (bool?)null,
                    note = detailed ? field.Note : null
                })
            });
        return Task.FromResult(AssistantToolOutcome.Ok(new
        {
            runOrder = WorkflowNodeCatalog.RunOrder,
            connectionRules = WorkflowNodeCatalog.ConnectionRules,
            nodes
        }));
    }
}
