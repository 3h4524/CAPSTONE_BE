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
    public async Task<MediaJob?> ClaimAsync(DateTime now, Guid token, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE media_jobs SET status = 'failed', error_message = 'Worker lease expired after maximum attempts.', updated_at = {now} WHERE status = 'leased' AND lease_expires_at < {now} AND attempt >= maximum_attempts", ct);
        var jobs = await db.MediaJobs.FromSqlInterpolated($"WITH candidate AS (SELECT id FROM media_jobs WHERE (status = 'queued' AND available_at <= {now} OR status = 'leased' AND lease_expires_at < {now}) AND attempt < maximum_attempts ORDER BY available_at FOR UPDATE SKIP LOCKED LIMIT 1) UPDATE media_jobs j SET status = 'leased', attempt = attempt + 1, lease_token = {token}, lease_expires_at = {now.AddMinutes(2)}, heartbeat_at = {now}, updated_at = {now} FROM candidate c WHERE j.id = c.id RETURNING j.*").ToListAsync(ct);
        return jobs.SingleOrDefault();
    }
}
