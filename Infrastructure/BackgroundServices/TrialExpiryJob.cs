using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Marks free trials whose end date has passed as expired, every hour.
/// </summary>
/// <remarks>
/// Access already ends on time without this job: <c>GetActiveWithPlanAsync</c> ignores an ended
/// trial. The job only keeps the stored status honest for the Admin Portal's counts.
/// </remarks>
public sealed class TrialExpiryJob(
    IServiceProvider serviceProvider,
    ILogger<TrialExpiryJob> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        try
        {
            await ExpireEndedTrialsAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ExpireEndedTrialsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("TrialExpiryJob is stopping.");
        }
    }

    private async Task ExpireEndedTrialsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = timeProvider.GetUtcNow().UtcDateTime;

            var expired = await dbContext.Subscriptions
                .Where(subscription => subscription.Status == "active"
                    && subscription.TrialEndsAt != null
                    && subscription.TrialEndsAt <= now)
                .ExecuteUpdateAsync(
                    update => update
                        .SetProperty(subscription => subscription.Status, "expired")
                        .SetProperty(subscription => subscription.UpdatedAt, now),
                    cancellationToken);

            if (expired > 0)
            {
                logger.LogInformation("Expired {Count} ended trials.", expired);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "An error occurred while expiring trials.");
        }
    }
}
