using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Workflows;
using APCS.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Plans the child runs of confirmed run groups, a few at a time, each in a fresh scope that acts as the run's
/// owner. Woken when a group is confirmed; also looks every minute, which finds the runs a restart left queued or
/// half planned (<see cref="RunGroupPlanner.StalledAfter"/>).
/// </summary>
/// <remarks>
/// A run is taken and marked in one short transaction (<see cref="IRunGroupPlanner.ClaimNextAsync"/>), so it shows as
/// working at once; its AI scene prompts, then its listing, are written after that with nothing locked.
/// </remarks>
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
                // Each part in a scope of its own: the slow parts run with no transaction open, and the short transaction
                // that ends each one must read the run afresh, not the copy an earlier part left in its context.
                GroupRunClaim? claim;
                using (var scope = scopes.CreateScope())
                    claim = await scope.ServiceProvider.GetRequiredService<IRunGroupPlanner>()
                        .ClaimNextAsync(scope.ServiceProvider.GetRequiredService<ActingUserContext>().ActAs, stoppingToken);
                advanced = claim is not null;
                // The clips first: the video is what takes longest, and the listing is not part of it.
                if (claim is { PreparesPrompts: true })
                    await InScopeAsync((planner, actAs) => planner.PreparePromptsAsync(claim, actAs, stoppingToken));
                if (claim is { WritesListing: true })
                    await InScopeAsync((planner, actAs) => planner.WriteListingAsync(claim, actAs, stoppingToken));
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

    private async Task InScopeAsync(Func<IRunGroupPlanner, Action<Guid>, Task> part)
    {
        using var scope = scopes.CreateScope();
        await part(scope.ServiceProvider.GetRequiredService<IRunGroupPlanner>(),
            scope.ServiceProvider.GetRequiredService<ActingUserContext>().ActAs);
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
