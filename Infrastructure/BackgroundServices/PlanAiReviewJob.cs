using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Admin.SubscriptionPlans.AiReview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Writes the Plan Insights AI review when an Admin asks for one, and on its own once the last
/// review is older than the refresh interval (14 days by default).
/// </summary>
/// <remarks>
/// The schedule is checked every hour. A failed attempt is stored, and the next one waits the
/// configured retry delay, so a model outage costs one call a day rather than one an hour. Only
/// this job writes reviews, so two are never written at once.
/// </remarks>
public sealed class PlanAiReviewJob(
    IServiceProvider serviceProvider,
    IPlanAiReviewQueue queue,
    ILogger<PlanAiReviewJob> logger) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

    // Lets the app finish starting before the first, possibly minutes-long, review.
    private static readonly TimeSpan FirstCheckAfter = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var requested = await queue.WaitAsync(FirstCheckAfter, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunAsync(requested, stoppingToken);
                requested = await queue.WaitAsync(CheckEvery, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("PlanAiReviewJob is stopping.");
        }
    }

    private async Task RunAsync(Guid? requestedBy, CancellationToken cancellationToken)
    {
        // A request already marked the queue busy; this clears it however the run ends.
        using var requested = requestedBy is null ? null : queue.Generating();
        try
        {
            using var scope = serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IPlanAiReviewService>();
            if (requestedBy is null && !await service.IsDueAsync(cancellationToken)) return;

            using (queue.Generating())
            {
                await service.GenerateAsync(requestedBy, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "An error occurred while writing the plan AI review.");
        }
    }
}
