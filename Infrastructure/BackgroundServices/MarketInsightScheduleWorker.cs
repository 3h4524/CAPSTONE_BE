using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Commerce;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class MarketInsightScheduleWorker(
    IServiceProvider serviceProvider,
    IMarketResearchQueue queue,
    TimeProvider timeProvider,
    ILogger<MarketInsightScheduleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);
        await TryEnqueueDueSchedulesAsync(stoppingToken);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TryEnqueueDueSchedulesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task TryEnqueueDueSchedulesAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var schedules = scope.ServiceProvider.GetRequiredService<IMarketInsightScheduleService>();
        var now = timeProvider.GetUtcNow();
        foreach (var userId in await schedules.GetScheduledUserIdsAsync(cancellationToken))
        {
            var schedule = await schedules.GetForUserAsync(userId, cancellationToken);
            if (!schedule.Enabled
                || schedule.Queries.Count == 0
                || (schedule.NextRunAtUtc is not null && schedule.NextRunAtUtc > now))
                continue;

            try
            {
                var job = queue.Enqueue(userId, new MarketResearchRequest(
                    schedule.Platform,
                    schedule.Queries,
                    schedule.LimitPerQuery,
                    schedule.MinimumViralScore,
                    schedule.TopN));
                await schedules.MarkRunAsync(userId, now, job.JobId, cancellationToken);
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
            }
        }
    }
}
