using APCS.Application.Features.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>Every half minute, fails the runs whose render or export job failed without the worker reporting it.</summary>
public sealed class WorkflowRunFailureSweeper(IServiceScopeFactory scopes, ILogger<WorkflowRunFailureSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<IWorkflowRunReconciler>();
                var failed = await reconciler.FailRunsWithFailedJobsAsync(stoppingToken);
                if (failed > 0) logger.LogInformation("Marked {Count} workflow runs failed after their media job failed.", failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Workflow run failure sweep failed.");
            }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
