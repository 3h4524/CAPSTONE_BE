using System;
using System.Threading;
using System.Threading.Tasks;
using APCS.Domain.Entities;
using APCS.Infrastructure.BackgroundServices;
using APCS.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Infrastructure.UnitTests.Persistence;

[TestClass]
public sealed class MediaJobQueuedInterceptorTests
{
    private readonly WorkflowActivitySignal activity = new();
    private readonly AfterCommitInterceptor afterCommit = new();

    [TestMethod]
    [DataRow("render")]
    [DataRow("export_zip")]
    public async Task SaveChanges_QueuedMediaJobWithoutTransaction_SignalsOnceSaved(string kind)
    {
        await using var db = Context();
        db.MediaJobs.Add(Job(kind, "queued"));

        await db.SaveChangesAsync();

        activity.Media.Version.Should().Be(1);
    }

    [TestMethod]
    [DataRow("video_quality", "queued")]
    [DataRow("render", "leased")]
    public async Task SaveChanges_NoRenderOrExportQueued_DoesNotSignal(string kind, string status)
    {
        await using var db = Context();
        db.MediaJobs.Add(Job(kind, status));

        await db.SaveChangesAsync();

        activity.Media.Version.Should().Be(0);
    }

    [TestMethod]
    public void Defer_InTransaction_RunsOnlyAfterTheCommitAndOnce()
    {
        using var db = Context();
        var runs = 0;

        afterCommit.Defer(db, () => runs++, inTransaction: true);
        runs.Should().Be(0);

        afterCommit.Committed(db);
        afterCommit.Committed(db);
        runs.Should().Be(1, "one commit runs its actions once");
    }

    [TestMethod]
    public void Defer_RolledBackOrAbandoned_NeverRuns()
    {
        using var db = Context();
        var runs = 0;
        afterCommit.Defer(db, () => runs++, inTransaction: true);
        afterCommit.Drop(db);
        afterCommit.Committed(db);

        runs.Should().Be(0);
    }

    [TestMethod]
    public void Defer_OutsideTransaction_RunsAtOnce()
    {
        using var db = Context();
        var runs = 0;

        afterCommit.Defer(db, () => runs++);

        runs.Should().Be(1);
    }

    [TestMethod]
    public async Task PublishQueue_OutsideTransaction_ReachesTheChannelAtOnce()
    {
        using var db = Context();
        var channel = new WorkflowPublishQueueChannel(activity);
        var queue = new TransactionalWorkflowPublishQueue(channel, db, afterCommit);
        var step = Guid.NewGuid();

        queue.Enqueue(step);

        (await channel.DequeueAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(step);
        activity.Publish.Version.Should().Be(1);
    }

    private AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(afterCommit, new MediaJobQueuedInterceptor(activity, afterCommit))
            .Options);

    private static MediaJob Job(string kind, string status) => new()
    {
        Id = Guid.NewGuid(), WorkflowRunId = Guid.NewGuid(), WorkflowNodeRunId = Guid.NewGuid(), UserId = Guid.NewGuid(),
        Kind = kind, Status = status, Payload = "{}", MaximumAttempts = 3,
    };
}
