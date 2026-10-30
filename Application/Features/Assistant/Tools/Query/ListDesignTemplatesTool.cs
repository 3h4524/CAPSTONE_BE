using APCS.Application.Features.DesignTemplates;
using APCS.Application.Features.DesignTemplates.Common;
using APCS.Application.Features.DesignTemplates.Dtos.Request;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed record ListDesignTemplatesArgs(string? Search, string? Scope);

public sealed class ListDesignTemplatesTool(IDesignTemplateService templates) : AssistantTool<ListDesignTemplatesArgs>
{
    private const int MaxItems = 20;

    public override string Name => "list_design_templates";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description =>
        "List design templates (id, name, niche, art style) for the Prompt synthesis step. Use the returned id as designTemplateId.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"search":{"type":"string","description":"Optional text to search by name."},"scope":{"type":"string","enum":["system","personal","all"],"description":"Default system."}}}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, ListDesignTemplatesArgs arguments, CancellationToken cancellationToken)
    {
        var scope = arguments.Scope is { } requested && DesignTemplateScopes.Values.Contains(requested) ? requested : DesignTemplateScopes.System;
        var result = await templates.ListAsync(new ListDesignTemplatesRequestDto(1, MaxItems, arguments.Search, null, null, scope), cancellationToken);
        if (result.IsFailure)
            return QueryToolSupport.Failure(result.Error);

        return AssistantToolOutcome.Ok(new
        {
            total = result.Value.TotalCount,
            templates = result.Value.Items.Take(MaxItems).Select(item => new
            {
                id = item.Id,
                name = item.Name,
                niche = item.NicheCategory,
                artStyle = item.ArtStyle,
                isSystem = item.IsSystemTemplate
            })
        });
    }
}
