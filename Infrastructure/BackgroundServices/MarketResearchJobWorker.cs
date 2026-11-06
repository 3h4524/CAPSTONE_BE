using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Commerce;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

public sealed class MarketResearchJobWorker(
    IMarketResearchQueue queue,
    IServiceProvider serviceProvider,
    ILogger<MarketResearchJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Market research job worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            MarketResearchJobWorkItem job;
            try
            {
                job = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            queue.MarkRunning(job.JobId);
            try
            {
                using var scope = serviceProvider.CreateScope();
                var result = await scope.ServiceProvider
                    .GetRequiredService<IMarketResearchService>()
                    .RetrieveAndIngestAsync(job.Request, stoppingToken);
                queue.MarkCompleted(job.JobId, result);
                logger.LogInformation(
                    "Market research job {JobId} completed for {Query}: retrieved {RetrievedCount}, selected {SelectedCount}.",
                    job.JobId,
                    job.Request.Queries.FirstOrDefault(),
                    result.RetrievedCount,
                    result.SelectedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                queue.MarkFailed(job.JobId, exception.Message);
                logger.LogError(exception, "Market research job {JobId} failed.", job.JobId);
            }
        }

        logger.LogInformation("Market research job worker is stopping.");
    }
}
