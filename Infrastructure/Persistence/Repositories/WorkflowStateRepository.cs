using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence.Repositories;

public sealed class WorkflowStateRepository(AppDbContext db) : IWorkflowStateRepository
{
    public async Task LockOwnerAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ownerId.ToString()}, 0))", cancellationToken);
    public Task<WorkflowRun?> LockRunAsync(Guid id, CancellationToken ct) => db.WorkflowRuns.FromSqlInterpolated($"SELECT * FROM public.workflow_runs WHERE id = {id} FOR UPDATE").FirstOrDefaultAsync(ct);
    public Task<Workflow?> LockWorkflowAsync(Guid id, CancellationToken ct) => db.Workflows.FromSqlInterpolated($"SELECT * FROM public.workflows WHERE id = {id} FOR UPDATE").FirstOrDefaultAsync(ct);
    public Task<MockupImage?> LockMockupAsync(Guid id, CancellationToken ct) => db.MockupImages.FromSqlInterpolated($"SELECT * FROM public.mockup_images WHERE id = {id} FOR UPDATE").FirstOrDefaultAsync(ct);
    public async Task<MediaJob?> LockJobAsync(Guid id, CancellationToken ct)
    {
        var tracked = db.ChangeTracker.Entries<MediaJob>().FirstOrDefault(e => e.Entity.Id == id);
        if (tracked != null) tracked.State = EntityState.Detached;
        return await db.MediaJobs.FromSqlInterpolated($"SELECT * FROM media_jobs WHERE id = {id} FOR UPDATE").FirstOrDefaultAsync(ct);
    }
    public Task<MediaJob?> ClaimAsync(DateTime now, Guid token, CancellationToken ct) =>
        ClaimAsync(now, token, "media", ct);

    public async Task<MediaJob?> ClaimAsync(DateTime now, Guid token, string capability, CancellationToken ct)
    {
        var qualityOnly = string.Equals(capability, "video_quality", StringComparison.Ordinal);
        // An exhausted render or export job is settled together with its run by IMediaLeaseRecovery before a media
        // claim. A video quality job has no run to keep in step, so it is still failed here in one update.
        if (qualityOnly)
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE media_jobs SET status = 'failed', error_message = 'Worker lease expired after maximum attempts.', updated_at = {now} WHERE status = 'leased' AND lease_expires_at < {now} AND attempt >= maximum_attempts AND kind = 'video_quality'", ct);
        var jobs = qualityOnly
            ? await db.MediaJobs.FromSqlInterpolated($"WITH candidate AS (SELECT id FROM media_jobs WHERE kind = 'video_quality' AND (status = 'queued' AND available_at <= {now} OR status = 'leased' AND lease_expires_at < {now}) AND attempt < maximum_attempts ORDER BY available_at FOR UPDATE SKIP LOCKED LIMIT 1) UPDATE media_jobs j SET status = 'leased', attempt = attempt + 1, lease_token = {token}, lease_expires_at = {now.AddMinutes(2)}, heartbeat_at = {now}, updated_at = {now} FROM candidate c WHERE j.id = c.id RETURNING j.*").ToListAsync(ct)
            : await db.MediaJobs.FromSqlInterpolated($"WITH candidate AS (SELECT id FROM media_jobs WHERE kind IN ('render', 'export_zip') AND (status = 'queued' AND available_at <= {now} OR status = 'leased' AND lease_expires_at < {now}) AND attempt < maximum_attempts ORDER BY available_at FOR UPDATE SKIP LOCKED LIMIT 1) UPDATE media_jobs j SET status = 'leased', attempt = attempt + 1, lease_token = {token}, lease_expires_at = {now.AddMinutes(2)}, heartbeat_at = {now}, updated_at = {now} FROM candidate c WHERE j.id = c.id RETURNING j.*").ToListAsync(ct);
        return jobs.SingleOrDefault();
    }

    public async Task<MediaJob?> ClaimMediaAsync(Guid jobId, long queueVersion, DateTime now, Guid token, CancellationToken ct)
    {
        var rows = await db.MediaJobs.FromSqlInterpolated($"UPDATE media_jobs SET status = 'leased', attempt = attempt + 1, lease_token = {token}, lease_expires_at = {now.AddMinutes(2)}, heartbeat_at = {now}, updated_at = {now} WHERE id = {jobId} AND queue_version = {queueVersion} AND kind IN ('render', 'export_zip') AND status = 'queued' AND available_at <= {now} AND attempt < maximum_attempts RETURNING *").ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    public async Task<IReadOnlyList<FailedMediaJob>> FindFailedJobsOfRunningRunsAsync(int limit, CancellationToken ct) =>
        await db.MediaJobs.AsNoTracking()
            .Where(job => job.Status == "failed" && (job.Kind == "render" || job.Kind == "export_zip") && job.WorkflowRun.Status == "running")
            .OrderBy(job => job.UpdatedAt)
            .Select(job => new FailedMediaJob(job.WorkflowRunId, job.WorkflowNodeRunId, job.ErrorMessage))
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ExpiredMediaLease>> FindExpiredMediaLeasesAsync(DateTime now, int limit, CancellationToken ct) =>
        await db.MediaJobs.AsNoTracking()
            .Where(job => (job.Kind == "render" || job.Kind == "export_zip") && job.Status == "leased" && job.LeaseExpiresAt < now)
            .OrderBy(job => job.LeaseExpiresAt)
            .Select(job => new ExpiredMediaLease(job.Id, job.WorkflowRunId))
            .Take(limit)
            .ToListAsync(ct);

    public Task<bool> HasMediaWorkInFlightAsync(DateTime updatedSince, CancellationToken ct) =>
        db.MediaJobs.AsNoTracking().AnyAsync(job => (job.Kind == "render" || job.Kind == "export_zip")
            && (job.Status == "queued" || job.Status == "leased") && job.UpdatedAt >= updatedSince, ct);

    public async Task<string> GetMediaClaimStatusAsync(Guid jobId, long queueVersion, DateTime now, CancellationToken ct) =>
        await db.Database.SqlQuery<string>($"SELECT COALESCE((SELECT CASE WHEN status = 'leased' THEN 'busy' WHEN status = 'queued' AND available_at > {now} AND attempt < maximum_attempts THEN 'not_due' ELSE 'obsolete' END FROM media_jobs WHERE id = {jobId} AND queue_version = {queueVersion} AND kind IN ('render', 'export_zip')), 'obsolete') AS \"Value\"").SingleAsync(ct);
}
