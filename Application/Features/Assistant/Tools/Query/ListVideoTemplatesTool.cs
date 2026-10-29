using APCS.Application.Features.Workflows;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed class ListVideoTemplatesTool(IVideoPlanningService planning) : AssistantTool<NoArguments>
{
    public override string Name => "list_video_templates";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description =>
        "List the Standard promo video templates with their default duration, transition and requirements.";

    protected override string ParametersSchemaJson => """{"type":"object","properties":{}}""";

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, NoArguments arguments, CancellationToken cancellationToken)
    {
        var result = await planning.ListTemplatesAsync(cancellationToken);
        if (result.IsFailure)
            return QueryToolSupport.Failure(result.Error);

        return AssistantToolOutcome.Ok(new
        {
            templates = result.Value.Select(item => new
            {
                code = item.Code,
                name = item.Name,
                description = item.Description,
                defaultDurationSeconds = item.DefaultDurationSeconds,
                defaultTransition = item.DefaultTransition,
                minimumAssets = item.Requirements.MinimumAssets
            })
        });
    }
}
