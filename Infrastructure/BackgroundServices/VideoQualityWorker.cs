using APCS.Application.Features.Workflows;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>Runs expensive VBench and OCR evidence outside the seller's HTTP request.</summary>
public sealed class VideoQualityWorker(IServiceScopeFactory scopes, IOptions<VideoQualityOptions> options,
    ILogger<VideoQualityWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.Value;
            // AutoEvaluate controls queue consumption. Enabled only controls whether the evidence
            // runner invokes local scripts; disabled tooling produces a completed "skipped" result.
            if (!settings.AutoEvaluate)
            {
                await DelayAsync(settings, stoppingToken);
                continue;
            }

            try
            {
                using var scope = scopes.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IVideoQualityJobService>();
                if (!await jobs.ProcessNextAsync(stoppingToken)) await DelayAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Video-quality worker iteration failed.");
                await DelayAsync(settings, stoppingToken);
            }
        }
    }

    private static Task DelayAsync(VideoQualityOptions settings, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.PollIntervalSeconds, 1, 60)), cancellationToken);
}
