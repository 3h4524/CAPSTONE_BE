using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Rag.Common;
using APCS.Application.Features.Rag.Dtos;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed record SearchFaqArgs(string? Query, int? TopK);

public sealed class SearchFaqTool(IRagService ragService) : AssistantTool<SearchFaqArgs>
{
    private const int DefaultTopK = 5;
    private const int MaxTopK = 8;
    private const int MaxContentChars = 1500;

    public override string Name => "search_faq";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description =>
        "Search the APCS help center for how-to, rules, limits, pricing and troubleshooting answers. Always call this before answering product questions. The help articles are written in English, so write the query in English even when the user writes in another language. Make the query stand alone: resolve follow-ups such as 'what about the other one' or 'why did it fail' from the conversation, and name the feature, step or setting they refer to.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"query":{"type":"string","description":"A short, standalone search query in English that names the exact feature, step or setting, filled in from the earlier messages when the latest one is a follow-up. Translate the user's question first if it is in another language."},"topK":{"type":"integer","description":"Number of articles to return, 1 to 8."}},"required":["query"]}
        """;

    public override bool IsAvailable(AssistantToolContext context) => true;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, SearchFaqArgs arguments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(arguments.Query))
            return AssistantToolOutcome.Error("invalid_arguments", "query is required.");

        IReadOnlyList<RagSearchResultDto> results;
        try
        {
            var topK = Math.Clamp(arguments.TopK ?? DefaultTopK, 1, MaxTopK);
            results = await ragService.SearchAsync(new RagSearchRequest(arguments.Query.Trim(), topK, ContentType: RagContentTypes.Faq), cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            return AssistantToolOutcome.Error("faq_unavailable", "The help center is not available right now.");
        }

        if (results.Count == 0)
        {
            return AssistantToolOutcome.Ok(new
            {
                results = Array.Empty<object>(),
                note = "No help article matched. Say you do not have that information."
            });
        }

        var citations = results
            .GroupBy(result => result.DocumentId)
            .Select(group => new AssistantCitationData(group.Key, group.First().Title, group.First().SourceUrl))
            .ToArray();
        return AssistantToolOutcome.Ok(
            new
            {
                results = results.Select(result => new
                {
                    title = result.Title,
                    content = QueryToolSupport.Clip(result.Content, MaxContentChars),
                    sourceUrl = result.SourceUrl,
                    score = Math.Round(result.Similarity, 3)
                })
            },
            false,
            AssistantStreamEvent.Citations(citations));
    }
}
