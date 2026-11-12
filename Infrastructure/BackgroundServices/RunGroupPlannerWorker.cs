using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Workflows;
using APCS.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Plans the child runs of confirmed run groups, a few at a time, each in a fresh scope that acts as the run's
/// owner. Woken when a group is confirmed; also looks every minute, which finds the runs a restart left queued.
/// </summary>
public sealed class RunGroupPlannerWorker(IServiceScopeFactory scopes, IRunGroupPlannerQueue queue,
    ILogger<RunGroupPlannerWorker> logger) : BackgroundService
{
    /// <summary>Child runs planned at the same time.</summary>
    public const int Concurrency = 3;

    private static readonly TimeSpan LookEvery = TimeSpan.FromMinutes(1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ => Task.Run(() => LoopAsync(stoppingToken), stoppingToken)));

    private async Task LoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var advanced = false;
            try
            {
                using var scope = scopes.CreateScope();
                var acting = scope.ServiceProvider.GetRequiredService<ActingUserContext>();
                advanced = await scope.ServiceProvider.GetRequiredService<IRunGroupPlanner>().AdvanceNextAsync(acting.ActAs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RunPlanningException exception)
            {
                logger.LogError(exception.InnerException, "Run {RunId} of a run group could not be planned.", exception.RunId);
                await FailAsync(exception.RunId, stoppingToken);
                advanced = true;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Run group planner iteration failed.");
            }

            if (advanced) continue;
            try { await queue.WaitAsync(LookEvery, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task FailAsync(Guid runId, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IRunGroupPlanner>()
                .FailAsync(runId, "The video could not be planned. Retry it from its run.", stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Run {RunId} could not be marked failed.", runId);
        }
    }
}
