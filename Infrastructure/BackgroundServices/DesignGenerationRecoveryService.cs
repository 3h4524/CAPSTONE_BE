using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Common.Constants;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>
/// Puts jobs that were queued or running when the process stopped back on the work queue. The queue
/// lives in memory, so after a restart nothing would pick them up: the job would stay "running" for
/// ever and its batch could not start another one.
/// </summary>
/// <remarks>
/// <c>ProcessBatchJobAsync</c> only handles products still marked as generating, so a job that was
/// part-way resumes with the remaining products. Assumes one application instance: with several, each
/// would also take the others' running jobs.
/// </remarks>
public sealed class DesignGenerationRecoveryService(
    IServiceProvider serviceProvider,
    IDesignGenerationQueue queue,
    ILogger<DesignGenerationRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var interrupted = await FindInterruptedJobIdsAsync(dbContext, cancellationToken);

            foreach (var batchJobId in interrupted)
                queue.Enqueue(batchJobId);

            if (interrupted.Count > 0)
                logger.LogInformation("Re-queued {Count} design generation job(s) interrupted by a restart.", interrupted.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The app must start even when the database is briefly unreachable.
            logger.LogError(ex, "Could not re-queue interrupted design generation jobs.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static Task<List<Guid>> FindInterruptedJobIdsAsync(AppDbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.BatchJobs
            .AsNoTracking()
            .Where(job => job.DeletedAt == null
                && (job.Status == BatchJobStatuses.Queued || job.Status == BatchJobStatuses.Running))
            .OrderBy(job => job.StartedAt)
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);
}
