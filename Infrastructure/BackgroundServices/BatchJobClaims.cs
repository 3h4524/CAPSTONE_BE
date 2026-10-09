using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Common.Constants;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.BackgroundServices;

/// <summary>Claims a batch job with one conditional update of its row.</summary>
public sealed class BatchJobClaims(AppDbContext dbContext, TimeProvider timeProvider) : IBatchJobClaims
{
    /// <summary>
    /// A running job whose worker has not reported for this long is taken to be abandoned (its instance
    /// stopped). A worker reports before every image, and one image is capped at three minutes.
    /// </summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(5);

    public async Task<bool> TryClaimAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var abandonedBefore = now - AbandonedAfter;
        var claimable = dbContext.BatchJobs.Where(job => job.Id == batchJobId && job.DeletedAt == null
            && (job.Status == BatchJobStatuses.Queued
                || (job.Status == BatchJobStatuses.Running && (job.UpdatedAt == null || job.UpdatedAt < abandonedBefore))));

        if (dbContext.Database.IsRelational())
        {
            // A single UPDATE: when two workers try at the same moment, the second one finds the row already
            // running and freshly reported, and changes nothing.
            return await claimable.ExecuteUpdateAsync(job => job
                .SetProperty(x => x.Status, BatchJobStatuses.Running)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken) == 1;
        }

        // The in-memory provider of the unit tests has no set-based update.
        var found = await claimable.SingleOrDefaultAsync(cancellationToken);
        if (found is null)
            return false;

        found.Status = BatchJobStatuses.Running;
        found.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        // The caller works with its own copy of the job, which must not collide with this one.
        dbContext.Entry(found).State = EntityState.Detached;
        return true;
    }
}
