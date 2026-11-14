using APCS.Application.Features.BatchMockups.Printify;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Runs the Printify measuring jobs one at a time, each in its own scope: a job makes and deletes products in the
/// platform's Printify store, so two at once would only race for Printify's rate limit.
/// </summary>
public sealed class PrintifyTemplateJobWorker(
    PrintifyTemplateJobChannel queue,
    IServiceProvider services,
    ILogger<PrintifyTemplateJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid jobId;
            try
            {
                jobId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IPrintifyTemplateBuilder>().RunAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Printify template job {JobId} could not be run.", jobId);
            }
        }
    }
}
