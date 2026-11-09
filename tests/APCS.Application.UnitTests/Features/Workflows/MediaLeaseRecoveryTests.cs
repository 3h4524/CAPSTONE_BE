using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class MediaLeaseRecoveryTests
{
    private static readonly DateTime Now = WorkflowTestData.Now.UtcDateTime;

    [TestMethod]
    public async Task RecoverExpiredAsync_LastAttemptExpired_FailsJobStepAndRunTogether()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var job = Expired(fixture.AddJob(run), attempt: 3);

        var settled = await Recovery(fixture).RecoverExpiredAsync(Now, default);

        settled.Should().Be(1);
        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Be(MediaLeaseRecovery.ExhaustedMessage);
        job.LeaseToken.Should().BeNull();
        run.Status.Should().Be("failed", "a run must never say running over a failed job");
        run.ErrorMessage.Should().Be(MediaLeaseRecovery.ExhaustedMessage);
        fixture.NodeRows.Single(x => x.Id == job.WorkflowNodeRunId).Status.Should().Be("failed");
        fixture.Transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RecoverExpiredAsync_AttemptsLeft_RequeuesJobAndKeepsRunRunning()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var job = Expired(fixture.AddJob(run), attempt: 1);

        await Recovery(fixture).RecoverExpiredAsync(Now, default);

        job.Status.Should().Be("queued");
        job.AvailableAt.Should().Be(Now);
        job.LeaseExpiresAt.Should().BeNull();
        run.Status.Should().Be("running");
    }

    [TestMethod]
    public async Task RecoverExpiredAsync_RunNoLongerRunning_CancelsJob()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var job = Expired(fixture.AddJob(run), attempt: 3);
        run.Status = "cancelled";

        await Recovery(fixture).RecoverExpiredAsync(Now, default);

        job.Status.Should().Be("cancelled");
        run.Status.Should().Be("cancelled");
    }

    [TestMethod]
    public async Task RecoverExpiredAsync_LeaseRenewedSinceRead_LeavesJob()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var job = Expired(fixture.AddJob(run), attempt: 3);
        var recovery = Recovery(fixture);
        // Read as expired, then a heartbeat renewed the lease before the lock was taken.
        fixture.State.Setup(x => x.FindExpiredMediaLeasesAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ExpiredMediaLease(job.Id, run.Id)]);
        job.LeaseExpiresAt = Now.AddMinutes(2);

        var settled = await recovery.RecoverExpiredAsync(Now, default);

        settled.Should().Be(0);
        job.Status.Should().Be("leased");
        run.Status.Should().Be("running");
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RecoverExpiredAsync_TwoExpiredJobsOfOneRun_SettlesOnePerPass()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        Expired(fixture.AddJob(run), attempt: 1);
        Expired(fixture.AddJob(run, "export_zip"), attempt: 1);
        var recovery = Recovery(fixture);

        (await recovery.RecoverExpiredAsync(Now, default)).Should().Be(1);
        (await recovery.RecoverExpiredAsync(Now, default)).Should().Be(1);
        fixture.JobRows.Should().OnlyContain(job => job.Status == "queued");
    }

    [TestMethod]
    public async Task ClaimAsync_Polling_SettlesExpiredLeasesBeforeClaiming()
    {
        var fixture = new WorkflowTestData();
        var recovery = new Mock<IMediaLeaseRecovery>();
        var order = new List<string>();
        recovery.Setup(x => x.RecoverExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("recover")).ReturnsAsync(0);
        fixture.State.Setup(x => x.ClaimAsync(It.IsAny<DateTime>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("claim")).ReturnsAsync((MediaJob?)null);

        var result = await Service(fixture, recovery.Object).ClaimAsync(default);

        result.IsSuccess.Should().BeTrue();
        order.Should().Equal("recover", "claim");
    }

    [TestMethod]
    public async Task ClaimStreamAsync_LeavesRecoveryToTheDispatcher()
    {
        var fixture = new WorkflowTestData();
        var recovery = new Mock<IMediaLeaseRecovery>();

        await Service(fixture, recovery.Object).ClaimStreamAsync(Guid.NewGuid(), 1, default);

        recovery.Verify(x => x.RecoverExpiredAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static MediaJob Expired(MediaJob job, int attempt)
    {
        job.Attempt = attempt;
        job.LeaseExpiresAt = Now.AddSeconds(-1);
        return job;
    }

    // The repository query, answered from the fixture's rows as the database would.
    private static MediaLeaseRecovery Recovery(WorkflowTestData fixture)
    {
        fixture.State.Setup(x => x.FindExpiredMediaLeasesAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime now, int limit, CancellationToken _) => (IReadOnlyList<ExpiredMediaLease>)fixture.JobRows
                .Where(job => job.Kind is "render" or "export_zip" && job.Status == "leased" && job.LeaseExpiresAt < now)
                .OrderBy(job => job.LeaseExpiresAt)
                .Select(job => new ExpiredMediaLease(job.Id, job.WorkflowRunId))
                .Take(limit).ToArray());
        return new(fixture.State.Object, fixture.Unit.Object, fixture.Runtime);
    }

    private static MediaJobService Service(WorkflowTestData fixture, IMediaLeaseRecovery recovery) =>
        new(fixture.Jobs.Object, fixture.Nodes.Object, fixture.Assets.Object, fixture.Videos.Object, fixture.Scenes.Object,
            fixture.Exports.Object, fixture.Items.Object, fixture.State.Object, fixture.Unit.Object, fixture.Storage.Object,
            fixture.Runtime, fixture.Time, localExports: fixture.LocalExports.Object, leaseRecovery: recovery);
}
