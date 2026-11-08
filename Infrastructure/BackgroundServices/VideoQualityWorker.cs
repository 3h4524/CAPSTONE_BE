using APCS.Application.Features.Workflows;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>Runs expensive VBench and OCR evidence outside the seller's HTTP request.</summary>
/// <remarks>
/// The queue is a database table, so finding a job means querying it. While jobs keep coming the worker
/// polls every <see cref="VideoQualityOptions.PollIntervalSeconds"/>; each empty poll doubles the wait up to
/// <see cref="VideoQualityOptions.MaxIdlePollSeconds"/>, so an idle database is left alone long enough to
/// suspend. Any claimed job resets the wait.
/// </remarks>
public sealed class VideoQualityWorker(IServiceScopeFactory scopes, IOptions<VideoQualityOptions> options,
    ILogger<VideoQualityWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idlePolls = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.Value;
            // AutoEvaluate controls queue consumption. Enabled only controls whether the evidence
            // runner invokes local scripts; disabled tooling produces a completed "skipped" result.
            if (!settings.AutoEvaluate)
            {
                await Task.Delay(IdleDelay(settings, 0), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                continue;
            }

            try
            {
                using var scope = scopes.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IVideoQualityJobService>();
                if (await jobs.ProcessNextAsync(stoppingToken))
                {
                    idlePolls = 0;
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Video-quality worker iteration failed.");
            }

            // An empty queue and a failed poll both back off: a database that is down or over quota
            // is not helped by being asked again every few seconds.
            await Task.Delay(IdleDelay(settings, idlePolls), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            idlePolls = Math.Min(idlePolls + 1, 30);
        }
    }

    /// <summary>The poll interval doubled once per consecutive empty poll, capped at the idle maximum.</summary>
    internal static TimeSpan IdleDelay(VideoQualityOptions settings, int idlePolls)
    {
        var baseSeconds = Math.Clamp(settings.PollIntervalSeconds, 1, 60);
        var maxSeconds = Math.Max(baseSeconds, settings.MaxIdlePollSeconds);
        var seconds = Math.Min(maxSeconds, baseSeconds * Math.Pow(2, idlePolls));
        return TimeSpan.FromSeconds(seconds);
    }
}
