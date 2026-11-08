using APCS.Application.Abstractions.Caching;
using APCS.Application.Features.Commerce;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

public sealed class MarketResearchScheduleService(ICacheService cache, IOptions<MarketResearchOptions> options, TimeProvider timeProvider) : IMarketResearchScheduleService
{
    private const string CacheKey = "admin:market-research:schedule";
    // Each query costs one Etsy request a day, so the limit is about collection time, not quota.
    private const int MaximumQueries = 300;
    private const int DefaultBatchSize = 5;
    private const int DefaultRunWindowMinutes = 120;
    // A daily collection is only useful as a time series, so the window may cover a month of days.
    private const int MaximumRunWindowMinutes = 43200;

    public async Task<MarketResearchSchedule> GetAsync(CancellationToken cancellationToken = default)
    {
        var stored = await cache.GetAsync<StoredSchedule>(CacheKey, cancellationToken);
        return ToResponse(stored ?? FromOptions(options.Value));
    }

    public async Task<MarketResearchSchedule> UpdateAsync(UpdateMarketResearchScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var queries = request.Queries.Where(query => !string.IsNullOrWhiteSpace(query)).Select(query => query.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumQueries).ToArray();
        if (request.Enabled && queries.Length == 0) throw new ArgumentException("At least one query is required when the schedule is enabled.", nameof(request));
        if (request.LimitPerQuery is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(request.LimitPerQuery));
        if (request.MinimumViralScore is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(request.MinimumViralScore));
        if (request.TopN is < 1 or > 25) throw new ArgumentOutOfRangeException(nameof(request.TopN));
        if (request.IntervalMinutes is < 1 or > 10080) throw new ArgumentOutOfRangeException(nameof(request.IntervalMinutes));
        if (request.BatchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(request.BatchSize));
        if (request.RunWindowMinutes is < 1 or > MaximumRunWindowMinutes) throw new ArgumentOutOfRangeException(nameof(request.RunWindowMinutes));

        var updated = new StoredSchedule(
            request.Enabled,
            request.Platform,
            queries,
            request.LimitPerQuery,
            request.MinimumViralScore,
            request.TopN,
            request.IntervalMinutes,
            null,
            request.Enabled ? timeProvider.GetUtcNow() : null,
            request.BatchSize,
            request.RunWindowMinutes,
            0);
        await cache.SetAsync(CacheKey, updated, TimeSpan.FromDays(365), cancellationToken);
        return ToResponse(updated);
    }

    public async Task MarkRunAsync(DateTimeOffset runAtUtc, int processedQueryCount, CancellationToken cancellationToken = default)
    {
        var current = await cache.GetAsync<StoredSchedule>(CacheKey, cancellationToken) ?? FromOptions(options.Value);
        var queryCount = current.Queries.Count;
        var nextOffset = queryCount == 0
            ? 0
            : (current.NextQueryOffset + Math.Max(1, processedQueryCount)) % queryCount;
        var windowStartedAtUtc = current.WindowStartedAtUtc ?? runAtUtc;
        await cache.SetAsync(
            CacheKey,
            current with
            {
                LastRunAtUtc = runAtUtc,
                WindowStartedAtUtc = windowStartedAtUtc,
                NextQueryOffset = nextOffset
            },
            TimeSpan.FromDays(365),
            cancellationToken);
    }

    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        var current = await cache.GetAsync<StoredSchedule>(CacheKey, cancellationToken);
        if (current is null || !current.Enabled)
            return;

        await cache.SetAsync(
            CacheKey,
            current with { Enabled = false },
            TimeSpan.FromDays(365),
            cancellationToken);
    }

    private MarketResearchSchedule ToResponse(StoredSchedule stored)
    {
        var topN = stored.TopN is < 1 or > 25 ? 3 : stored.TopN;
        var batchSize = stored.BatchSize is < 1 or > 100 ? DefaultBatchSize : stored.BatchSize;
        var runWindowMinutes = stored.RunWindowMinutes is < 1 or > MaximumRunWindowMinutes ? DefaultRunWindowMinutes : stored.RunWindowMinutes;
        DateTimeOffset? next = stored.Enabled && stored.LastRunAtUtc is not null ? stored.LastRunAtUtc.Value.AddMinutes(stored.IntervalMinutes) : stored.Enabled ? timeProvider.GetUtcNow() : null;
        DateTimeOffset? windowEnds = stored.Enabled && stored.WindowStartedAtUtc is not null
            ? stored.WindowStartedAtUtc.Value.AddMinutes(runWindowMinutes)
            : null;
        return new(stored.Enabled, stored.Platform, stored.Queries, stored.LimitPerQuery, stored.MinimumViralScore, topN, stored.IntervalMinutes, stored.LastRunAtUtc, next, null, batchSize, runWindowMinutes, stored.WindowStartedAtUtc, windowEnds, stored.NextQueryOffset);
    }

    private static StoredSchedule FromOptions(MarketResearchOptions value) => new(
        value.Enabled,
        value.Platform,
        value.Queries,
        value.LimitPerQuery,
        value.MinimumViralScore,
        value.TopN,
        value.IntervalMinutes,
        null,
        null,
        value.BatchSize,
        value.RunWindowMinutes,
        0);

    private sealed record StoredSchedule(
        bool Enabled,
        APCS.Application.Abstractions.Commerce.CommercePlatform Platform,
        IReadOnlyList<string> Queries,
        int LimitPerQuery,
        double MinimumViralScore,
        int TopN,
        int IntervalMinutes,
        DateTimeOffset? LastRunAtUtc,
        DateTimeOffset? WindowStartedAtUtc,
        int BatchSize = DefaultBatchSize,
        int RunWindowMinutes = DefaultRunWindowMinutes,
        int NextQueryOffset = 0);
}
