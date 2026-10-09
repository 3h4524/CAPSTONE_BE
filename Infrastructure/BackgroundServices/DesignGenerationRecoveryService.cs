using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Common.Constants;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Puts unfinished jobs back on the work queue. The queue lives in memory, so after a restart nothing
/// would pick them up: the job would stay "running" for ever and its batch could not start another one.
/// </summary>
/// <remarks>
/// <para>
/// When the application starts, every queued or running job is put back. After that the jobs are looked at
/// again every minute, and the ones nobody has reported on for a while are put back too: a job whose worker
/// stopped only counts as abandoned after <see cref="BatchJobClaims.AbandonedAfter"/>, so right after a
/// restart it is still too early to resume it.
/// </para>
/// <para>
/// Several instances may share a database and each runs this. Putting a job on a queue never starts it by
/// itself: the worker has to claim it first (<see cref="IBatchJobClaims"/>), and a job another instance is
/// working on cannot be claimed. <c>ProcessBatchJobAsync</c> only handles products still marked as
/// generating, so a job that was part-way resumes with the remaining products.
/// </para>
/// </remarks>
public sealed class DesignGenerationRecoveryService(
    IServiceProvider serviceProvider,
    IDesignGenerationQueue queue,
    TimeProvider timeProvider,
    ILogger<DesignGenerationRecoveryService> logger) : BackgroundService
{
    internal static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Created first, so the minute is counted from the start rather than from the end of the first pass.
        using var timer = new PeriodicTimer(CheckEvery, timeProvider);
        await RequeueAsync(onlyAbandoned: false, stoppingToken);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RequeueAsync(onlyAbandoned: true, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // The application is stopping.
        }
    }

    internal async Task RequeueAsync(bool onlyAbandoned, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            DateTime? abandonedBefore = onlyAbandoned ? timeProvider.GetUtcNow().UtcDateTime - BatchJobClaims.AbandonedAfter : null;
            var unfinished = await FindUnfinishedJobIdsAsync(dbContext, abandonedBefore, cancellationToken);

            foreach (var batchJobId in unfinished)
                queue.Enqueue(batchJobId);

            if (unfinished.Count > 0)
                logger.LogInformation("Re-queued {Count} unfinished design generation job(s).", unfinished.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The app must start, and keep running, even when the database is briefly unreachable.
            logger.LogError(ex, "Could not re-queue unfinished design generation jobs.");
        }
    }

    /// <summary>
    /// The queued and running jobs, oldest first. With <paramref name="abandonedBefore"/> only the ones not
    /// reported on since then: the rest are waiting in a queue or being worked on.
    /// </summary>
    internal static Task<List<Guid>> FindUnfinishedJobIdsAsync(AppDbContext dbContext, DateTime? abandonedBefore, CancellationToken cancellationToken) =>
        dbContext.BatchJobs
            .AsNoTracking()
            .Where(job => job.DeletedAt == null
                && (job.Status == BatchJobStatuses.Queued || job.Status == BatchJobStatuses.Running)
                && (abandonedBefore == null || job.UpdatedAt == null || job.UpdatedAt < abandonedBefore))
            .OrderBy(job => job.StartedAt)
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);
}
