using APCS.Application.Features.Commerce;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class MarketResearchSyncWorker(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<MarketResearchSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);
        await TrySyncAsync(stoppingToken);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await TrySyncAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task TrySyncAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var schedules = scope.ServiceProvider.GetRequiredService<IMarketResearchScheduleService>();
        var schedule = await schedules.GetAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!schedule.Enabled || schedule.Queries.Count == 0)
            return;

        if (schedule.WindowEndsAtUtc is not null && schedule.WindowEndsAtUtc <= now)
        {
            await schedules.DisableAsync(cancellationToken);
            logger.LogInformation("Scheduled market research window ended after {RunWindowMinutes} minutes.", schedule.RunWindowMinutes);
            return;
        }

        if (schedule.NextRunAtUtc is not null && schedule.NextRunAtUtc > now)
            return;

        var batchSize = Math.Max(1, schedule.BatchSize);
        try
        {
            // Trends compare one observation per query and day, so a query already observed
            // today is skipped until the next UTC day instead of being sampled again.
            var research = scope.ServiceProvider.GetRequiredService<IMarketResearchService>();
            var pending = await research.SelectUnobservedQueriesAsync(
                schedule.Platform,
                schedule.Queries,
                DateOnly.FromDateTime(now.UtcDateTime),
                cancellationToken);
            if (pending.Count == 0)
                return;

            var batch = pending.Take(batchSize).ToArray();
            var result = await research.RetrieveAndIngestAsync(new MarketResearchRequest(
                schedule.Platform, batch, schedule.LimitPerQuery, schedule.MinimumViralScore, schedule.TopN), cancellationToken);
            await schedules.MarkRunAsync(now, batch.Length, cancellationToken);
            logger.LogInformation("Scheduled market research collected {BatchCount} queries ({RemainingCount} left today of {QueryCount}), retrieved {RetrievedCount} and queued {SelectedCount} listing references.", batch.Length, pending.Count - batch.Length, schedule.Queries.Count, result.RetrievedCount, result.SelectedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Scheduled market research sync failed.");
        }
    }
}
