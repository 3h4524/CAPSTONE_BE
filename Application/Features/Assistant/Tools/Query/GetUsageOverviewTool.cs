using APCS.Application.Features.UsageStatistics;

namespace APCS.Application.Features.Assistant.Tools.Query;

public sealed record UsageOverviewArgs(int? Days);

public sealed class GetUsageOverviewTool(IUsageStatisticsService usage) : AssistantTool<UsageOverviewArgs>
{
    private static readonly int[] AllowedDays = [7, 30, 90];

    public override string Name => "get_usage_overview";

    public override AssistantToolKind Kind => AssistantToolKind.Query;

    protected override string Description => "Get the user's usage statistics for the last 7, 30 or 90 days.";

    protected override string ParametersSchemaJson => """
        {"type":"object","properties":{"days":{"type":"integer","description":"Time window: 7, 30 or 90 days (default 30)."}}}
        """;

    public override bool IsAvailable(AssistantToolContext context) => context.Caller.IsSeller;

    protected override async Task<AssistantToolOutcome> ExecuteCoreAsync(AssistantToolContext context, UsageOverviewArgs arguments, CancellationToken cancellationToken)
    {
        var days = arguments.Days is { } requested && AllowedDays.Contains(requested) ? requested : 30;
        var result = await usage.GetOverviewAsync(days, cancellationToken);
        return result.IsFailure
            ? QueryToolSupport.Failure(result.Error)
            : AssistantToolOutcome.Ok(new { days, summary = result.Value.Summary, allowance = result.Value.Allowance, costs = result.Value.Costs, hasData = result.Value.HasData });
    }
}
