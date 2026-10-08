using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using APCS.Infrastructure.BackgroundServices;
using APCS.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace APCS.Infrastructure.UnitTests.Services;

[TestClass]
public sealed class DesignGenerationRecoveryServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task StartAsync_RequeuesOnlyJobsThatWereQueuedOrRunning()
    {
        var databaseName = Guid.NewGuid().ToString();
        var queued = SeedJob(databaseName, BatchJobStatuses.Queued);
        var running = SeedJob(databaseName, BatchJobStatuses.Running);
        SeedJob(databaseName, BatchJobStatuses.Draft);
        SeedJob(databaseName, BatchJobStatuses.Completed);
        SeedJob(databaseName, BatchJobStatuses.Failed);
        SeedJob(databaseName, BatchJobStatuses.Running, deleted: true);
        var queue = new Mock<IDesignGenerationQueue>();

        await CreateService(databaseName, queue.Object).StartAsync(CancellationToken.None);

        queue.Verify(x => x.Enqueue(queued), Times.Once);
        queue.Verify(x => x.Enqueue(running), Times.Once);
        queue.Verify(x => x.Enqueue(It.IsAny<Guid>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task StartAsync_WithNothingInterrupted_EnqueuesNothing()
    {
        var databaseName = Guid.NewGuid().ToString();
        SeedJob(databaseName, BatchJobStatuses.Completed);
        var queue = new Mock<IDesignGenerationQueue>();

        await CreateService(databaseName, queue.Object).StartAsync(CancellationToken.None);

        queue.Verify(x => x.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [TestMethod]
    public async Task StartAsync_WhenTheDatabaseCannotBeReached_DoesNotStopTheApplicationFromStarting()
    {
        var queue = new Mock<IDesignGenerationQueue>();
        var noDatabase = new ServiceCollection().BuildServiceProvider();
        var service = new DesignGenerationRecoveryService(noDatabase, queue.Object, NullLogger<DesignGenerationRecoveryService>.Instance);

        var act = () => service.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        queue.Verify(x => x.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    private static DesignGenerationRecoveryService CreateService(string databaseName, IDesignGenerationQueue queue)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return new DesignGenerationRecoveryService(services.BuildServiceProvider(), queue, NullLogger<DesignGenerationRecoveryService>.Instance);
    }

    private static Guid SeedJob(string databaseName, string status, bool deleted = false)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options);
        var id = Guid.NewGuid();
        db.BatchJobs.Add(new BatchJob
        {
            Id = id, UserId = Guid.NewGuid(), BatchId = Guid.NewGuid(), Name = "J", SourceFileType = "manual", Status = status,
            JobType = "design_generation", Config = "{}", Priority = "normal", CreatedAt = Now, StartedAt = Now,
            DeletedAt = deleted ? Now : null
        });
        db.SaveChanges();
        return id;
    }
}
