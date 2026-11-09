using APCS.Application.Features.Commerce;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Collects the admin's scheduled market research, one batch of queries per run, within the schedule's window.
/// Between runs it sleeps until the next one is due (the next run, the next UTC day once today is collected, or the
/// end of the window), or until the schedule is saved (<see cref="MarketScheduleSignal.Research"/>).
/// </summary>
public sealed class MarketResearchSyncWorker(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<MarketResearchSyncWorker> logger,
    MarketScheduleSignal? signal = null) : BackgroundService
{
    private readonly MarketScheduleSignal signals = signal ?? new MarketScheduleSignal();

    // The UTC day and query list whose collection is complete. Until either changes, a pass needs no
    // database query to learn there is nothing left.
    private (DateOnly Day, string Queries)? completed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var seen = signals.Research.Version;
                var nextDue = await TrySyncAsync(stoppingToken);
                var wait = MarketScheduleSignal.Until(nextDue, timeProvider.GetUtcNow());
                await signals.Research.WaitAsync(seen, wait, stoppingToken, timeProvider);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Collects a batch when one is due and returns when to look again; null while the schedule is off.</summary>
    internal async Task<DateTimeOffset?> TrySyncAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        try
        {
            using var scope = serviceProvider.CreateScope();
            var schedules = scope.ServiceProvider.GetRequiredService<IMarketResearchScheduleService>();
            var schedule = await schedules.GetAsync(cancellationToken);
            if (!schedule.Enabled || schedule.Queries.Count == 0)
                return null;

            if (schedule.WindowEndsAtUtc is not null && schedule.WindowEndsAtUtc <= now)
            {
                await schedules.DisableAsync(cancellationToken);
                logger.LogInformation("Scheduled market research window ended after {RunWindowMinutes} minutes.", schedule.RunWindowMinutes);
                return null;
            }

            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var queries = string.Join('\n', schedule.Queries);
            if (completed is { } done && done.Day == today && done.Queries == queries)
                return DoneForToday(schedule, now);

            if (schedule.NextRunAtUtc is { } dueAt && dueAt > now)
                return Earliest(dueAt, schedule.WindowEndsAtUtc);

            // Trends compare one observation per query and day, so a query already observed
            // today is skipped until the next UTC day instead of being sampled again.
            var research = scope.ServiceProvider.GetRequiredService<IMarketResearchService>();
            var pending = await research.SelectUnobservedQueriesAsync(
                schedule.Platform,
                schedule.Queries,
                today,
                cancellationToken);
            if (pending.Count == 0)
            {
                completed = (today, queries);
                return DoneForToday(schedule, now);
            }

            var batch = pending.Take(Math.Max(1, schedule.BatchSize)).ToArray();
            var result = await research.RetrieveAndIngestAsync(new MarketResearchRequest(
                schedule.Platform, batch, schedule.LimitPerQuery, schedule.MinimumViralScore, schedule.TopN), cancellationToken);
            await schedules.MarkRunAsync(now, batch.Length, cancellationToken);
            logger.LogInformation("Scheduled market research collected {BatchCount} queries ({RemainingCount} left today of {QueryCount}), retrieved {RetrievedCount} and queued {SelectedCount} listing references.", batch.Length, pending.Count - batch.Length, schedule.Queries.Count, result.RetrievedCount, result.SelectedCount);
            return Earliest(now.AddMinutes(schedule.IntervalMinutes), schedule.WindowEndsAtUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Scheduled market research sync failed.");
            return now + MarketScheduleSignal.RetryAfter;
        }
    }

    /// <summary>Today is collected: the next run is due tomorrow at the earliest, unless the window ends first.</summary>
    private static DateTimeOffset DoneForToday(MarketResearchSchedule schedule, DateTimeOffset now)
    {
        var tomorrow = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        var due = schedule.NextRunAtUtc is { } next && next > tomorrow ? next : tomorrow;
        return Earliest(due, schedule.WindowEndsAtUtc);
    }

    private static DateTimeOffset Earliest(DateTimeOffset due, DateTimeOffset? windowEnds) =>
        windowEnds is { } end && end < due ? end : due;
}
