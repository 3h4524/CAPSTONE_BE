using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using APCS.Infrastructure.BackgroundServices;
using APCS.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class DesignGenerationRecoveryServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task RequeueAsync_AtStart_PutsBackEveryJobThatWasQueuedOrRunning()
    {
        var databaseName = Guid.NewGuid().ToString();
        var queued = SeedJob(databaseName, BatchJobStatuses.Queued);
        // Reported on a moment ago: at start nothing is in the queue yet, so it is put back all the same.
        var running = SeedJob(databaseName, BatchJobStatuses.Running, reportedMinutesAgo: 0);
        SeedJob(databaseName, BatchJobStatuses.Draft);
        SeedJob(databaseName, BatchJobStatuses.Completed);
        SeedJob(databaseName, BatchJobStatuses.Failed);
        SeedJob(databaseName, BatchJobStatuses.Running, deleted: true);
        var queue = new Mock<IDesignGenerationQueue>();

        await CreateService(databaseName, queue.Object).RequeueAsync(onlyAbandoned: false, CancellationToken.None);

        queue.Verify(x => x.Enqueue(queued), Times.Once);
        queue.Verify(x => x.Enqueue(running), Times.Once);
        queue.Verify(x => x.Enqueue(It.IsAny<Guid>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task RequeueAsync_Later_PutsBackOnlyTheJobsNobodyHasReportedOnForAWhile()
    {
        var databaseName = Guid.NewGuid().ToString();
        var abandonedRunning = SeedJob(databaseName, BatchJobStatuses.Running, reportedMinutesAgo: 10);
        var abandonedQueued = SeedJob(databaseName, BatchJobStatuses.Queued, reportedMinutesAgo: 10);
        var neverReported = SeedJob(databaseName, BatchJobStatuses.Queued);
        // Being worked on, or just queued: left where they are.
        SeedJob(databaseName, BatchJobStatuses.Running, reportedMinutesAgo: 1);
        SeedJob(databaseName, BatchJobStatuses.Queued, reportedMinutesAgo: 1);
        SeedJob(databaseName, BatchJobStatuses.Completed, reportedMinutesAgo: 10);
        var queue = new Mock<IDesignGenerationQueue>();

        await CreateService(databaseName, queue.Object).RequeueAsync(onlyAbandoned: true, CancellationToken.None);

        queue.Verify(x => x.Enqueue(abandonedRunning), Times.Once);
        queue.Verify(x => x.Enqueue(abandonedQueued), Times.Once);
        queue.Verify(x => x.Enqueue(neverReported), Times.Once);
        queue.Verify(x => x.Enqueue(It.IsAny<Guid>()), Times.Exactly(3));
    }

    [TestMethod]
    public async Task RequeueAsync_WithNothingUnfinished_EnqueuesNothing()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedJob(databaseName, BatchJobStatuses.Completed);
        var queue = new Mock<IDesignGenerationQueue>();

        await CreateService(databaseName, queue.Object).RequeueAsync(onlyAbandoned: false, CancellationToken.None);

        queue.Verify(x => x.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [TestMethod]
    public async Task RequeueAsync_WhenTheDatabaseCannotBeReached_DoesNotThrow()
    {
        var queue = new Mock<IDesignGenerationQueue>();
        var noDatabase = new ServiceCollection().BuildServiceProvider();
        var service = new DesignGenerationRecoveryService(noDatabase, queue.Object, new FakeTimeProvider(Now), NullLogger<DesignGenerationRecoveryService>.Instance);

        var act = () => service.RequeueAsync(onlyAbandoned: false, CancellationToken.None);

        await act.Should().NotThrowAsync();
        queue.Verify(x => x.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [TestMethod]
    public async Task Service_PutsUnfinishedJobsBackWhenItStartsAndLooksAgainEveryMinute()
    {
        var databaseName = Guid.NewGuid().ToString();
        var abandoned = SeedJob(databaseName, BatchJobStatuses.Running, reportedMinutesAgo: 30);
        var time = new FakeTimeProvider(Now);
        using var enqueued = new SemaphoreSlim(0);
        var queue = new Mock<IDesignGenerationQueue>();
        queue.Setup(x => x.Enqueue(abandoned)).Callback(() => enqueued.Release());
        using var service = CreateService(databaseName, queue.Object, time);

        await service.StartAsync(CancellationToken.None);
        var atStart = await enqueued.WaitAsync(TimeSpan.FromSeconds(30));
        time.Advance(DesignGenerationRecoveryService.CheckEvery);
        var aMinuteLater = await enqueued.WaitAsync(TimeSpan.FromSeconds(30));
        await service.StopAsync(CancellationToken.None);

        atStart.Should().BeTrue();
        aMinuteLater.Should().BeTrue("a job that could not be claimed at start is offered again once it counts as abandoned");
    }

    private static DesignGenerationRecoveryService CreateService(string databaseName, IDesignGenerationQueue queue, TimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return new DesignGenerationRecoveryService(services.BuildServiceProvider(), queue, time ?? new FakeTimeProvider(Now), NullLogger<DesignGenerationRecoveryService>.Instance);
    }

    private static Guid SeedJob(string databaseName, string status, int? reportedMinutesAgo = null, bool deleted = false)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options);
        var id = Guid.NewGuid();
        db.BatchJobs.Add(new BatchJob
        {
            Id = id, UserId = Guid.NewGuid(), BatchId = Guid.NewGuid(), Name = "J", SourceFileType = "manual", Status = status,
            JobType = "design_generation", Config = "{}", Priority = "normal", CreatedAt = Now, StartedAt = Now,
            UpdatedAt = reportedMinutesAgo is int minutes ? Now.AddMinutes(-minutes) : null,
            DeletedAt = deleted ? Now : null
        });
        db.SaveChanges();
        return id;
    }
}
