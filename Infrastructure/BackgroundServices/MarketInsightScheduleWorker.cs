using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Commerce;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Queues each seller's background market insight collection when it is due. Between runs it sleeps until the
/// earliest schedule is due, or until a schedule is saved (<see cref="MarketScheduleSignal.Insights"/>).
/// </summary>
public sealed class MarketInsightScheduleWorker(
    IServiceProvider serviceProvider,
    IMarketResearchQueue queue,
    TimeProvider timeProvider,
    ILogger<MarketInsightScheduleWorker> logger,
    MarketScheduleSignal? signal = null) : BackgroundService
{
    private readonly MarketScheduleSignal signals = signal ?? new MarketScheduleSignal();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var seen = signals.Insights.Version;
                var nextDue = await TryEnqueueDueSchedulesAsync(stoppingToken);
                var wait = MarketScheduleSignal.Until(nextDue, timeProvider.GetUtcNow());
                await signals.Insights.WaitAsync(seen, wait, stoppingToken, timeProvider);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Queues every due schedule and returns when the earliest enabled one is next due, or null for none.</summary>
    internal async Task<DateTimeOffset?> TryEnqueueDueSchedulesAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        DateTimeOffset? earliest = null;
        void Consider(DateTimeOffset dueAt) => earliest = earliest is { } current && current <= dueAt ? current : dueAt;
        try
        {
            using var scope = serviceProvider.CreateScope();
            var schedules = scope.ServiceProvider.GetRequiredService<IMarketInsightScheduleService>();
            foreach (var userId in await schedules.GetScheduledUserIdsAsync(cancellationToken))
            {
                var schedule = await schedules.GetForUserAsync(userId, cancellationToken);
                if (!schedule.Enabled || schedule.Queries.Count == 0)
                    continue;
                if (schedule.NextRunAtUtc is { } dueAt && dueAt > now)
                {
                    Consider(dueAt);
                    continue;
                }

                try
                {
                    var job = queue.Enqueue(userId, new MarketResearchRequest(
                        schedule.Platform,
                        schedule.Queries,
                        schedule.LimitPerQuery,
                        schedule.MinimumViralScore,
                        schedule.TopN));
                    await schedules.MarkRunAsync(userId, now, job.JobId, cancellationToken);
                    Consider(now.AddMinutes(schedule.IntervalMinutes));
                    logger.LogInformation(
                        "Enqueued scheduled market insight job {JobId} for user {UserId}.",
                        job.JobId,
                        userId);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Could not enqueue scheduled market insights for user {UserId}.", userId);
                    Consider(now + MarketScheduleSignal.RetryAfter);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not read the scheduled market insights.");
            return now + MarketScheduleSignal.RetryAfter;
        }
        return earliest;
    }
}
