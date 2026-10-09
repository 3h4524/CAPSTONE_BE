using APCS.Common.Constants;
using APCS.Domain.Entities;
using APCS.Infrastructure.BackgroundServices;
using APCS.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class BatchJobClaimsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task TryClaimAsync_QueuedJob_IsTakenAndMarkedRunning()
    {
        await using var db = CreateContext();
        var jobId = AddJob(db, BatchJobStatuses.Queued, reportedMinutesAgo: 30);

        var claimed = await Claims(db).TryClaimAsync(jobId);

        claimed.Should().BeTrue();
        db.ChangeTracker.Entries<BatchJob>().Should().BeEmpty("the worker goes on with its own copy of the job");
        var job = await db.BatchJobs.SingleAsync(j => j.Id == jobId);
        job.Status.Should().Be(BatchJobStatuses.Running);
        job.UpdatedAt.Should().Be(Now.UtcDateTime);
    }

    [TestMethod]
    public async Task TryClaimAsync_JobAnotherWorkerReportedOnLately_IsRefused()
    {
        await using var db = CreateContext();
        var jobId = AddJob(db, BatchJobStatuses.Running, reportedMinutesAgo: 1);

        var claimed = await Claims(db).TryClaimAsync(jobId);

        claimed.Should().BeFalse();
        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).UpdatedAt.Should().Be(Now.AddMinutes(-1).UtcDateTime);
    }

    [TestMethod]
    [DataRow(6, DisplayName = "not reported on for longer than a worker ever stays silent")]
    [DataRow(null, DisplayName = "never reported on")]
    public async Task TryClaimAsync_RunningJobItsWorkerAbandoned_IsTakenOver(int? reportedMinutesAgo)
    {
        await using var db = CreateContext();
        var jobId = AddJob(db, BatchJobStatuses.Running, reportedMinutesAgo);

        (await Claims(db).TryClaimAsync(jobId)).Should().BeTrue();

        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).UpdatedAt.Should().Be(Now.UtcDateTime);
    }

    [TestMethod]
    public async Task TryClaimAsync_TheSameQueuedJobTwice_OnlyTheFirstClaimWins()
    {
        await using var db = CreateContext();
        var jobId = AddJob(db, BatchJobStatuses.Queued, reportedMinutesAgo: 30);

        (await Claims(db).TryClaimAsync(jobId)).Should().BeTrue();
        (await Claims(db).TryClaimAsync(jobId)).Should().BeFalse("the first claim marked it running and reported on it");
    }

    [TestMethod]
    [DataRow(BatchJobStatuses.Draft)]
    [DataRow(BatchJobStatuses.Completed)]
    [DataRow(BatchJobStatuses.PartiallyCompleted)]
    [DataRow(BatchJobStatuses.Failed)]
    public async Task TryClaimAsync_JobThatIsNotWaitingToRun_IsRefused(string status)
    {
        await using var db = CreateContext();
        var jobId = AddJob(db, status, reportedMinutesAgo: 30);

        (await Claims(db).TryClaimAsync(jobId)).Should().BeFalse();

        (await db.BatchJobs.SingleAsync(j => j.Id == jobId)).Status.Should().Be(status);
    }

    [TestMethod]
    public async Task TryClaimAsync_DeletedOrUnknownJob_IsRefused()
    {
        await using var db = CreateContext();
        var deleted = AddJob(db, BatchJobStatuses.Queued, reportedMinutesAgo: 30, deleted: true);

        (await Claims(db).TryClaimAsync(deleted)).Should().BeFalse();
        (await Claims(db).TryClaimAsync(Guid.NewGuid())).Should().BeFalse();
    }

    private static BatchJobClaims Claims(AppDbContext db) => new(db, new FakeTimeProvider(Now));

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Guid AddJob(AppDbContext db, string status, int? reportedMinutesAgo, bool deleted = false)
    {
        var id = Guid.NewGuid();
        db.BatchJobs.Add(new BatchJob
        {
            Id = id, UserId = Guid.NewGuid(), BatchId = Guid.NewGuid(), Name = "J", SourceFileType = "manual", Status = status,
            JobType = "design_generation", Config = "{}", Priority = "normal", CreatedAt = Now.AddHours(-1).UtcDateTime,
            UpdatedAt = reportedMinutesAgo is int minutes ? Now.AddMinutes(-minutes).UtcDateTime : null,
            DeletedAt = deleted ? Now.UtcDateTime : null
        });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return id;
    }
}
