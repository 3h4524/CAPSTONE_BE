using APCS.Application.Features.StyleArtPresets;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed class ListStylePresetsTool(IStyleArtPresetService presets) : AssistantTool<NoArguments>
{
    private const int MaxItems = 30;

    public override string Name => "list_style_presets";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description =>
        "List art styles (id, name, description) for the Prompt synthesis step. Use the returned id as stylePresetId.";

    protected override string ParametersSchemaJson => """{"type":"object","properties":{}}""";

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, NoArguments arguments, CancellationToken cancellationToken)
    {
        var result = await presets.ListMineAsync(cancellationToken);
        if (result.IsFailure)
            return QueryToolSupport.Failure(result.Error);

        return AssistantToolOutcome.Ok(new
        {
            presets = result.Value.Take(MaxItems).Select(item => new
            {
                id = item.Id,
                name = item.Name,
                description = QueryToolSupport.Clip(item.Description, 160),
                isSystem = item.IsSystemTemplate
            })
        });
    }
}
