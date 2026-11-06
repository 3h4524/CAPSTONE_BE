using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.DesignGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Consumes <see cref="IDesignTextCheckQueue"/>: reads the text of each saved design, one at a time, in a fresh DI scope,
/// and keeps the result with the design. A failure only means that design has no check.
/// </summary>
public sealed class DesignTextCheckWorker(
    IDesignTextCheckQueue queue,
    IServiceProvider serviceProvider,
    ILogger<DesignTextCheckWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DesignTextCheckRequest request;
            try
            {
                request = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = serviceProvider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DesignTextCheckService>().CheckAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The text check of design {DesignImageId} failed; it is kept without one.", request.DesignImageId);
            }
        }
    }
}
