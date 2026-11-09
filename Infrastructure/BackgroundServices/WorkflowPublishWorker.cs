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
/// at start, then every few minutes while any is unfinished; with none, the recovery waits for
/// <see cref="WorkflowActivitySignal.Publish"/> instead of querying an idle database.
/// </summary>
public sealed class WorkflowPublishWorker(
    WorkflowPublishQueueChannel queue,
    IServiceProvider services,
    ILogger<WorkflowPublishWorker> logger,
    WorkflowActivitySignal? activity = null) : BackgroundService
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
                var seen = activity?.Publish.Version ?? 0;
                var unfinished = true;
                try
                {
                    using var scope = services.CreateScope();
                    var steps = await scope.ServiceProvider.GetRequiredService<IWorkflowPublishService>().UnfinishedAsync(stoppingToken);
                    foreach (var nodeRunId in steps)
                        queue.Enqueue(nodeRunId);
                    unfinished = steps.Count > 0;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Looking for unfinished publish steps failed.");
                }
                // Nothing to recover: a newly queued step wakes this; it is looked at once it has had time to fail.
                if (activity is not null && !unfinished)
                    await activity.Publish.WaitAsync(seen, WorkflowActivitySignal.IdleSafetyCheck, stoppingToken);
                await Task.Delay(RecoverEvery, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }
}
