using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.Commerce;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

public sealed class MarketResearchScheduleService(ICacheService cache, IOptions<MarketResearchOptions> options, TimeProvider timeProvider) : IMarketResearchScheduleService
{
    private const string CacheKey = "admin:market-research:schedule";

    public async Task<MarketResearchSchedule> GetAsync(CancellationToken cancellationToken = default)
    {
        var stored = await cache.GetAsync<StoredSchedule>(CacheKey, cancellationToken);
        return ToResponse(stored ?? FromOptions(options.Value));
    }

    public async Task<MarketResearchSchedule> UpdateAsync(UpdateMarketResearchScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var queries = request.Queries.Where(query => !string.IsNullOrWhiteSpace(query)).Select(query => query.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
        if (request.Enabled && queries.Length == 0) throw new ArgumentException("At least one query is required when the schedule is enabled.", nameof(request));
        if (request.LimitPerQuery is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(request.LimitPerQuery));
        if (request.MinimumViralScore is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(request.MinimumViralScore));
        if (request.TopN is < 1 or > 25) throw new ArgumentOutOfRangeException(nameof(request.TopN));
        if (request.IntervalMinutes is < 15 or > 10080) throw new ArgumentOutOfRangeException(nameof(request.IntervalMinutes));

        var current = await cache.GetAsync<StoredSchedule>(CacheKey, cancellationToken);
        var updated = new StoredSchedule(request.Enabled, request.Platform, queries, request.LimitPerQuery, request.MinimumViralScore, request.TopN, request.IntervalMinutes, current?.LastRunAtUtc);
        await cache.SetAsync(CacheKey, updated, TimeSpan.FromDays(365), cancellationToken);
        return ToResponse(updated);
    }

    public async Task MarkRunAsync(DateTimeOffset runAtUtc, CancellationToken cancellationToken = default)
    {
        var current = await cache.GetAsync<StoredSchedule>(CacheKey, cancellationToken) ?? FromOptions(options.Value);
        await cache.SetAsync(CacheKey, current with { LastRunAtUtc = runAtUtc }, TimeSpan.FromDays(365), cancellationToken);
    }

    private MarketResearchSchedule ToResponse(StoredSchedule stored)
    {
        var topN = stored.TopN is < 1 or > 25 ? 3 : stored.TopN;
        DateTimeOffset? next = stored.Enabled && stored.LastRunAtUtc is not null ? stored.LastRunAtUtc.Value.AddMinutes(stored.IntervalMinutes) : stored.Enabled ? timeProvider.GetUtcNow() : null;
        return new(stored.Enabled, stored.Platform, stored.Queries, stored.LimitPerQuery, stored.MinimumViralScore, topN, stored.IntervalMinutes, stored.LastRunAtUtc, next);
    }

    private static StoredSchedule FromOptions(MarketResearchOptions value) => new(value.Enabled, value.Platform, value.Queries, value.LimitPerQuery, value.MinimumViralScore, value.TopN, value.IntervalMinutes, null);

    private sealed record StoredSchedule(bool Enabled, APCS.Application.Abstractions.Commerce.CommercePlatform Platform, IReadOnlyList<string> Queries, int LimitPerQuery, double MinimumViralScore, int TopN, int IntervalMinutes, DateTimeOffset? LastRunAtUtc);
}
