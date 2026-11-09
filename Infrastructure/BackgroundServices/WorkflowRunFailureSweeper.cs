using APCS.Application.Features.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Safety net that fails the runs whose render or export job failed without their run following (see
/// <see cref="WorkflowRunReconciler"/>). It looks every half minute only while such a job is in flight; otherwise it
/// waits for <see cref="WorkflowActivitySignal.Media"/>, so an idle database is not queried and can scale to zero.
/// </summary>
public sealed class WorkflowRunFailureSweeper(IServiceScopeFactory scopes, WorkflowActivitySignal activity,
    TimeProvider time, ILogger<WorkflowRunFailureSweeper> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The first pass catches what happened before a restart and decides whether to keep watching.
        var inFlight = true;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var seen = activity.Media.Version;
                inFlight = await SweepAsync(inFlight, stoppingToken);
                if (!inFlight)
                    await activity.Media.WaitAsync(seen, WorkflowActivitySignal.IdleSafetyCheck, stoppingToken, time);

                // A job queued just now cannot have failed yet, so even a woken sweeper waits one interval first.
                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Fails what it finds and returns whether media work is still in flight; on an error, the previous answer.</summary>
    private async Task<bool> SweepAsync(bool previous, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkflowRunReconciler>();
            var failed = await reconciler.FailRunsWithFailedJobsAsync(stoppingToken);
            if (failed > 0) logger.LogInformation("Marked {Count} workflow runs failed after their media job failed.", failed);
            var since = time.GetUtcNow().UtcDateTime - WorkflowActivitySignal.InFlightWindow;
            return await reconciler.HasMediaWorkInFlightAsync(since, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Workflow run failure sweep failed.");
            return previous;
        }
    }
}
