using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Features.Commerce;

namespace APCS.Infrastructure.Services;

public sealed class MarketInsightScheduleService(
    ICacheService cache,
    ICurrentUser currentUser,
    IMarketResearchQueue queue,
    TimeProvider timeProvider,
    APCS.Infrastructure.BackgroundServices.MarketScheduleSignal? signal = null) : IMarketInsightScheduleService
{
    private const int DefaultLimitPerQuery = 25;
    private const int DefaultTopN = 3;
    private const double DefaultMinimumViralScore = 0;
    private const int MinimumIntervalMinutes = 1;
    private const int MaximumIntervalMinutes = 10080;
    private const string IndexKey = "market-insights:schedules:index";

    public Task<MarketResearchSchedule> GetForCurrentUserAsync(CancellationToken cancellationToken = default) =>
        GetForUserAsync(RequireUserId(), cancellationToken);

    public async Task<MarketResearchSchedule> UpdateForCurrentUserAsync(
        UpdateMarketInsightScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = RequireUserId();
        var query = NormalizeQuery(request.Query);
        if (request.Enabled && query.Length == 0)
            throw new ArgumentException("A keyword is required when background collection is enabled.", nameof(request));
        if (request.IntervalMinutes is < MinimumIntervalMinutes or > MaximumIntervalMinutes)
            throw new ArgumentOutOfRangeException(nameof(request.IntervalMinutes));

        var now = timeProvider.GetUtcNow();
        Guid? lastJobId = null;
        DateTimeOffset? lastRunAtUtc = null;
        if (request.Enabled)
        {
            var job = queue.Enqueue(userId, new MarketResearchRequest(
                CommercePlatform.Etsy,
                [query],
                DefaultLimitPerQuery,
                DefaultMinimumViralScore,
                DefaultTopN));
            lastJobId = job.JobId;
            lastRunAtUtc = now;
        }

        var stored = new StoredSchedule(
            request.Enabled,
            query,
            request.IntervalMinutes,
            lastRunAtUtc,
            lastJobId);
        await cache.SetAsync(CacheKey(userId), stored, TimeSpan.FromDays(365), cancellationToken);
        await UpdateIndexAsync(userId, request.Enabled, cancellationToken);
        // The worker sleeps until the next schedule is due; this one may be due sooner.
        signal?.Insights.Notify();
        return ToResponse(stored);
    }

    public async Task<MarketResearchSchedule> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var stored = await cache.GetAsync<StoredSchedule>(CacheKey(userId), cancellationToken)
            ?? new StoredSchedule(false, string.Empty, 1440, null, null);
        return ToResponse(stored);
    }

    public async Task<IReadOnlyList<Guid>> GetScheduledUserIdsAsync(CancellationToken cancellationToken = default)
    {
        return await cache.GetAsync<Guid[]>(IndexKey, cancellationToken) ?? [];
    }

    public async Task MarkRunAsync(
        Guid userId,
        DateTimeOffset runAtUtc,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var current = await cache.GetAsync<StoredSchedule>(CacheKey(userId), cancellationToken);
        if (current is null || !current.Enabled)
            return;

        await cache.SetAsync(
            CacheKey(userId),
            current with { LastRunAtUtc = runAtUtc, LastJobId = jobId },
            TimeSpan.FromDays(365),
            cancellationToken);
    }

    private async Task UpdateIndexAsync(Guid userId, bool enabled, CancellationToken cancellationToken)
    {
        var current = (await cache.GetAsync<Guid[]>(IndexKey, cancellationToken) ?? [])
            .ToHashSet();
        if (enabled)
            current.Add(userId);
        else
            current.Remove(userId);
        await cache.SetAsync(IndexKey, current.ToArray(), TimeSpan.FromDays(365), cancellationToken);
    }

    private MarketResearchSchedule ToResponse(StoredSchedule stored)
    {
        DateTimeOffset? nextRun = null;
        if (stored.Enabled)
            nextRun = stored.LastRunAtUtc?.AddMinutes(stored.IntervalMinutes) ?? timeProvider.GetUtcNow();
        return new(
            stored.Enabled,
            CommercePlatform.Etsy,
            string.IsNullOrWhiteSpace(stored.Query) ? [] : [stored.Query],
            DefaultLimitPerQuery,
            DefaultMinimumViralScore,
            DefaultTopN,
            stored.IntervalMinutes,
            stored.LastRunAtUtc,
            nextRun,
            stored.LastJobId);
    }

    private Guid RequireUserId() => currentUser.UserId
        ?? throw new UnauthorizedAccessException("Authentication is required.");

    private static string CacheKey(Guid userId) => $"market-insights:schedule:{userId:N}";

    private static string NormalizeQuery(string value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed record StoredSchedule(
        bool Enabled,
        string Query,
        int IntervalMinutes,
        DateTimeOffset? LastRunAtUtc,
        Guid? LastJobId);
}
