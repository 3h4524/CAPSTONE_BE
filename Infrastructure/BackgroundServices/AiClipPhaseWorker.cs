using APCS.Application.Features.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Makes the AI scenes of render jobs in <c>generating_clips</c>: submits them to the video provider, asks how far they are,
/// and stores the finished clips. Waiting for the provider costs next to nothing here, so several scenes are made at once and
/// the media worker, which holds the CPU, only gets a job to render once its clips are stored.
/// </summary>
/// <remarks>
/// Everything a pass decides is read from the database, so a restart loses nothing: scenes already submitted keep their
/// provider operation and are asked about again. While jobs wait it looks every
/// <see cref="AiClipPhaseSettings.PollIntervalSeconds"/>, or at once when <see cref="WorkflowActivitySignal.ClipPhase"/> is raised
/// (a job was saved: the provider called back about a scene, or a job was queued); with none it stops querying and waits for that
/// signal, so a serverless database can scale to zero. It runs whether or not the phase is enabled, to finish the jobs that were
/// already waiting when it was turned off.
/// </remarks>
public sealed class AiClipPhaseWorker(IServiceScopeFactory scopes, IOptions<AiClipPhaseSettings> options,
    WorkflowActivitySignal activity, ILogger<AiClipPhaseWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Read before the pass, so a job queued while it runs is not missed.
            var seen = activity.ClipPhase.Version;
            var hasJobs = true;
            try
            {
                hasJobs = await PassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "AI clip pass failed.");
            }

            try
            {
                // A job saved meanwhile (a callback saying a scene finished, a new job) starts the next pass at once; otherwise
                // the jobs still waiting are looked at again after the interval.
                await activity.ClipPhase.WaitAsync(seen, hasJobs ? options.Value.PollInterval : WorkflowActivitySignal.IdleSafetyCheck, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One pass; true while any job is still waiting for its scenes.</summary>
    private async Task<bool> PassAsync(CancellationToken ct)
    {
        AiClipPhasePlan plan;
        using (var scope = scopes.CreateScope())
            plan = await scope.ServiceProvider.GetRequiredService<IAiClipPhaseService>().PlanAsync(ct);
        if (plan.Work.Count > 0)
        {
            // Each call has a scope of its own: a database context is not used by two calls at once.
            await Parallel.ForEachAsync(plan.Work,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Value.MaxParallelCalls), CancellationToken = ct },
                async (work, token) =>
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<IAiClipPhaseService>().RunAsync(work, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // One scene's trouble must not stop the others; the next pass looks at it again.
                        logger.LogError(exception, "AI clip {Action} for scene {Scene} of job {Job} failed.", work.Action, work.SceneOrder, work.JobId);
                    }
                });
        }
        return plan.HasJobs;
    }
}
