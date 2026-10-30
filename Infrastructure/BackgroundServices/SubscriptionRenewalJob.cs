using APCS.Application.Features.Subscriptions.Common;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Moves unpaid paid subscriptions along their lifecycle every hour: past the renewal date they
/// become past due, and once the grace period ends they expire.
/// </summary>
/// <remarks>
/// Access already ends on time without this job: <c>GetActiveWithPlanAsync</c> ignores a plan
/// whose grace period ended. The job keeps the stored status honest, so revenue, MRR and the
/// subscriber counts stop counting a Seller who no longer pays.
/// </remarks>
public sealed class SubscriptionRenewalJob(
    IServiceProvider serviceProvider,
    ILogger<SubscriptionRenewalJob> logger,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        try
        {
            await UpdateUnpaidSubscriptionsAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await UpdateUnpaidSubscriptionsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("SubscriptionRenewalJob is stopping.");
        }
    }

    private async Task UpdateUnpaidSubscriptionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (pastDue, expired) = await RunAsync(dbContext, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

            if (pastDue > 0 || expired > 0)
            {
                logger.LogInformation(
                    "Marked {PastDue} subscriptions past due and expired {Expired} unpaid subscriptions.",
                    pastDue,
                    expired);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "An error occurred while updating unpaid subscriptions.");
        }
    }

    /// <summary>Applies one pass of the lifecycle and returns how many rows moved to each status.</summary>
    public static async Task<(int PastDue, int Expired)> RunAsync(
        AppDbContext dbContext,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(utcNow);
        var oldestUsableRenewalDate = RenewalRules.OldestUsableRenewalDate(today);

        // Expired first, so a plan that skipped past due entirely (the job was not running) goes
        // straight to expired. Its churn date is the renewal date: the day it stopped being paid.
        var lapsed = await dbContext.Subscriptions
            .Where(subscription => (subscription.Status == "active" || subscription.Status == "past_due")
                && subscription.TrialEndsAt == null
                && subscription.DeletedAt == null
                && subscription.RenewalDate < oldestUsableRenewalDate)
            .ToListAsync(cancellationToken);
        foreach (var subscription in lapsed)
        {
            subscription.Status = "expired";
            subscription.CancelledAt = subscription.RenewalDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            subscription.UpdatedAt = utcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var pastDue = await dbContext.Subscriptions
            .Where(subscription => subscription.Status == "active"
                && subscription.TrialEndsAt == null
                && subscription.DeletedAt == null
                && subscription.RenewalDate <= today)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(subscription => subscription.Status, "past_due")
                    .SetProperty(subscription => subscription.UpdatedAt, utcNow),
                cancellationToken);

        return (pastDue, lapsed.Count);
    }
}
