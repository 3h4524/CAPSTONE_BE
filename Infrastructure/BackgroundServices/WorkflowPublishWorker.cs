using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Workflows;
using APCS.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Processes the publish steps of workflow runs one at a time, each in a fresh scope that acts as the run's owner,
/// so the Printify and Etsy use cases see the seller they serve. Steps left running by a restart are found again
/// at start and every few minutes.
/// </summary>
public sealed class WorkflowPublishWorker(
    IWorkflowPublishQueue queue,
    IServiceProvider services,
    ILogger<WorkflowPublishWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RecoverEvery = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = RecoverAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid nodeRunId;
            try
            {
                nodeRunId = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = services.CreateScope();
                var publish = scope.ServiceProvider.GetRequiredService<IWorkflowPublishService>();
                if (await publish.OwnerOfAsync(nodeRunId, stoppingToken) is not { } ownerId)
                    continue;
                scope.ServiceProvider.GetRequiredService<ActingUserContext>().ActAs(ownerId);
                if (await publish.ProcessAsync(nodeRunId, stoppingToken) is { } wait)
                    queue.Enqueue(nodeRunId, wait);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The step stays running and the next recovery pass tries it again.
                logger.LogError(exception, "Publish step {NodeRunId} could not be processed.", nodeRunId);
            }
        }
    }

    private async Task RecoverAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = services.CreateScope();
                    foreach (var nodeRunId in await scope.ServiceProvider.GetRequiredService<IWorkflowPublishService>().UnfinishedAsync(stoppingToken))
                        queue.Enqueue(nodeRunId);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Looking for unfinished publish steps failed.");
                }
                await Task.Delay(RecoverEvery, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }
}
