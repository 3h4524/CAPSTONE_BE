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
        if (!schedule.Enabled || schedule.Queries.Count == 0 || (schedule.NextRunAtUtc is not null && schedule.NextRunAtUtc > now))
            return;

        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<IMarketResearchService>()
                .RetrieveAndIngestAsync(new MarketResearchRequest(
                    schedule.Platform, schedule.Queries, schedule.LimitPerQuery, schedule.MinimumViralScore, schedule.TopN), cancellationToken);
            await schedules.MarkRunAsync(now, cancellationToken);
            logger.LogInformation("Scheduled market research retrieved {RetrievedCount} and queued {SelectedCount} listing references.", result.RetrievedCount, result.SelectedCount);
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
