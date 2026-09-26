using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.DesignGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Consumes <see cref="IDesignGenerationQueue"/> and runs <see cref="IDesignGenerationService.ProcessBatchJobAsync"/>
/// for each queued batch job, one at a time, in a fresh DI scope (repositories are scoped, this
/// service is a singleton).
/// </summary>
public sealed class DesignGenerationWorker(
    IDesignGenerationQueue queue,
    IServiceProvider serviceProvider,
    ILogger<DesignGenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DesignGenerationWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid batchJobId;
            try
            {
                batchJobId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IDesignGenerationService>();
                await service.ProcessBatchJobAsync(batchJobId, stoppingToken);
            }
            catch (Exception ex)
            {
                // One bad batch job must not stop the worker from processing the next one.
                logger.LogError(ex, "Failed to process design generation for batch job {BatchJobId}.", batchJobId);
                try
                {
                    // The failed scope's DbContext still holds the broken changes, so use a fresh one.
                    using var recoveryScope = serviceProvider.CreateScope();
                    await recoveryScope.ServiceProvider.GetRequiredService<IDesignGenerationService>()
                        .FailJobAsync(batchJobId, $"Processing stopped unexpectedly: {ex.Message}", stoppingToken);
                }
                catch (Exception recoveryEx)
                {
                    logger.LogError(recoveryEx, "Could not mark batch job {BatchJobId} as failed.", batchJobId);
                }
            }
        }

        logger.LogInformation("DesignGenerationWorker is stopping.");
    }
}
