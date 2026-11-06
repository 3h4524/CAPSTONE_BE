using APCS.Application.Abstractions.VideoQuality;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.Services;

/// <summary>Renews the lease with a separate database scope during long model inference.</summary>
public sealed class VideoQualityLeaseHeartbeat(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<VideoQualityLeaseHeartbeat> logger) : IVideoQualityLeaseHeartbeat
{
    public async Task RunAsync(Guid jobId, Guid leaseToken, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = clock.GetUtcNow().UtcDateTime;
                var renewed = await db.MediaJobs.Where(job => job.Id == jobId && job.Kind == "video_quality" &&
                    job.Status == "leased" && job.LeaseToken == leaseToken && job.LeaseExpiresAt > now)
                    .ExecuteUpdateAsync(update => update.SetProperty(job => job.HeartbeatAt, now)
                        .SetProperty(job => job.LeaseExpiresAt, now.AddMinutes(2)), cancellationToken);
                if (renewed == 0) { logger.LogWarning("Quality job {JobId} no longer holds its lease", jobId); return; }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning(exception, "Could not renew quality job {JobId}", jobId); }
        }
    }
}
