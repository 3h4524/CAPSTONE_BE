using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.BatchMockups;
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
                await GenerateMockupsAsync(scope.ServiceProvider, batchJobId, stoppingToken);
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

    // The designs are saved by now, so a failure here must never fail the job: the mock-ups can
    // still be generated from the app (the call is idempotent).
    private async Task GenerateMockupsAsync(IServiceProvider scopedServices, Guid batchJobId, CancellationToken cancellationToken)
    {
        try
        {
            var mockups = scopedServices.GetRequiredService<IMockupTemplateService>();
            var result = await mockups.GenerateAllForJobAsync(batchJobId, cancellationToken);
            if (result.IsSuccess)
                logger.LogInformation("Generated {Count} mock-up(s) for batch job {BatchJobId}.", result.Value.GeneratedCount, batchJobId);
            else
                // Expected when the job failed or no template was selected.
                logger.LogDebug("No mock-ups generated for batch job {BatchJobId}: {Code}.", batchJobId, result.Error.Code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not generate mock-ups for batch job {BatchJobId}.", batchJobId);
        }
    }
}
