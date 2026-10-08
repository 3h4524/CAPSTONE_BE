using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.IntegrationTests;

[TestClass]
public sealed class MediaJobRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task ClaimAsync_PostgreSql_ClaimsOnlyAvailableQueuedJobAndSetsLease()
    {
        await using var fixture = await CreateAsync();
        if (fixture == null) return;
        var available = Job("queued", Now.AddSeconds(-10));
        var future = Job("queued", Now.AddSeconds(10));
        var active = Job("leased", Now.AddSeconds(-20)); active.LeaseExpiresAt = Now.AddMinutes(1);
        var cancelled = Job("cancelled", Now.AddSeconds(-30));
        fixture.Db.MediaJobs.AddRange(available, future, active, cancelled);
        await fixture.Db.SaveChangesAsync(); fixture.Db.ChangeTracker.Clear();
        var token = Guid.NewGuid();
        var claimed = await fixture.Repository.ClaimAsync(Now, token, default);
        Assert.IsNotNull(claimed);
        Assert.AreEqual(available.Id, claimed.Id);
        Assert.AreEqual("leased", claimed.Status);
        Assert.AreEqual(1, claimed.Attempt);
        Assert.AreEqual(token, claimed.LeaseToken);
        Assert.AreEqual(Now.AddMinutes(2), claimed.LeaseExpiresAt);
        Assert.AreEqual(Now, claimed.HeartbeatAt);
        var rows = await fixture.Db.MediaJobs.AsNoTracking().ToListAsync();
        Assert.AreEqual("queued", rows.Single(x => x.Id == future.Id).Status);
        Assert.AreEqual("cancelled", rows.Single(x => x.Id == cancelled.Id).Status);
        Assert.AreEqual(active.LeaseToken, rows.Single(x => x.Id == active.Id).LeaseToken);
    }

    [TestMethod]
    public async Task ClaimAsync_PostgreSql_ExpiredLeaseGetsNewTokenAndExhaustedJobFails()
    {
        await using var fixture = await CreateAsync();
        if (fixture == null) return;
        var expired = Job("leased", Now.AddMinutes(-5)); expired.Attempt = 1; expired.LeaseExpiresAt = Now.AddTicks(-1);
        var exhausted = Job("leased", Now.AddMinutes(-10)); exhausted.Attempt = 3; exhausted.LeaseExpiresAt = Now.AddSeconds(-1);
        fixture.Db.MediaJobs.AddRange(expired, exhausted);
        await fixture.Db.SaveChangesAsync(); fixture.Db.ChangeTracker.Clear();
        var oldToken = expired.LeaseToken; var replacement = Guid.NewGuid();
        var claimed = await fixture.Repository.ClaimAsync(Now, replacement, default);
        Assert.IsNotNull(claimed);
        Assert.AreEqual(expired.Id, claimed.Id);
        Assert.AreEqual(2, claimed.Attempt);
        Assert.AreEqual(replacement, claimed.LeaseToken);
        Assert.AreNotEqual(oldToken, claimed.LeaseToken);
        var failed = await fixture.Db.MediaJobs.AsNoTracking().SingleAsync(x => x.Id == exhausted.Id);
        Assert.AreEqual("failed", failed.Status);
        Assert.AreEqual(3, failed.Attempt);
        Assert.AreEqual("Worker lease expired after maximum attempts.", failed.ErrorMessage);
    }

    [TestMethod]
    public async Task LockJobAsync_PostgreSql_RefreshesTrackedLeaseBeforeCompletionFence()
    {
        await using var fixture = await CreateAsync();
        if (fixture == null) return;
        var tracked = Job("leased", Now); fixture.Db.MediaJobs.Add(tracked);
        await fixture.Db.SaveChangesAsync();
        var replacement = Guid.NewGuid();
        await fixture.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE pg_temp.media_jobs SET lease_token = {replacement}, status = 'cancelled' WHERE id = {tracked.Id}");
        var locked = await fixture.Repository.LockJobAsync(tracked.Id, default);
        Assert.IsNotNull(locked);
        Assert.AreEqual(replacement, locked.LeaseToken);
        Assert.AreEqual("cancelled", locked.Status);
        Assert.AreEqual(EntityState.Detached, fixture.Db.Entry(tracked).State);
        Assert.AreNotSame(tracked, locked);
    }

    private static MediaJob Job(string status, DateTime available) => new()
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), WorkflowRunId = Guid.NewGuid(), WorkflowNodeRunId = Guid.NewGuid(),
        Kind = "render", Status = status, Payload = "{}", Attempt = 0, MaximumAttempts = 3, AvailableAt = available,
        LeaseToken = status == "leased" ? Guid.NewGuid() : null, CreatedAt = Now, UpdatedAt = Now
    };

    private static async Task<Fixture?> CreateAsync()
    {
        var connection = Environment.GetEnvironmentVariable("APCS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection)) { Assert.Inconclusive("Set APCS_TEST_CONNECTION_STRING to run PostgreSQL tests."); return null; }
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE media_jobs (LIKE public.media_jobs INCLUDING ALL) ON COMMIT DROP");
        return new(db, transaction);
    }

    private sealed class Fixture(AppDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public WorkflowStateRepository Repository { get; } = new(db);
        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            await Db.DisposeAsync();
        }
    }
}
