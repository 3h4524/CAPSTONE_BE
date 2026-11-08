using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowRunReconcilerTests
{
    [TestMethod]
    public async Task FailRunsWithFailedJobsAsync_RunningRunWithFailedRender_FailsRunAndStep()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var job = Failed(fixture.AddJob(run));
        var reconciler = Reconciler(fixture);

        var count = await reconciler.FailRunsWithFailedJobsAsync(default);

        count.Should().Be(1);
        run.Status.Should().Be("failed");
        run.ErrorMessage.Should().Be(job.ErrorMessage);
        var step = fixture.NodeRows.Single(x => x.Id == job.WorkflowNodeRunId);
        step.Status.Should().Be("failed");
        step.ErrorMessage.Should().Be(job.ErrorMessage);
        fixture.Transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task FailRunsWithFailedJobsAsync_RunMovedOnSinceRead_LeavesIt()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var job = Failed(fixture.AddJob(run));
        // Read while running, then retried or cancelled before the lock was taken.
        fixture.State.Setup(x => x.FindFailedJobsOfRunningRunsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new FailedMediaJob(run.Id, job.WorkflowNodeRunId, job.ErrorMessage)]);
        run.Status = "cancelled";

        var count = await Reconciler(fixture, findFromRows: false).FailRunsWithFailedJobsAsync(default);

        count.Should().Be(0);
        run.Status.Should().Be("cancelled");
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task FailRunsWithFailedJobsAsync_TwoFailedJobsOfOneRun_FailsRunOnce()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        Failed(fixture.AddJob(run));
        Failed(fixture.AddJob(run, "export_zip"));

        var count = await Reconciler(fixture).FailRunsWithFailedJobsAsync(default);

        count.Should().Be(1);
        run.Status.Should().Be("failed");
        run.Revision.Should().Be(8);
    }

    [TestMethod]
    public async Task FailRunsWithFailedJobsAsync_NothingFailed_DoesNothing()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        fixture.AddJob(run);

        var count = await Reconciler(fixture).FailRunsWithFailedJobsAsync(default);

        count.Should().Be(0);
        run.Status.Should().Be("running");
        fixture.Unit.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static MediaJob Failed(MediaJob job)
    {
        job.Status = "failed"; job.LeaseToken = null; job.LeaseExpiresAt = null;
        job.ErrorMessage = "Worker lease expired after maximum attempts.";
        return job;
    }

    // The repository query, answered from the fixture's rows as the database would.
    private static WorkflowRunReconciler Reconciler(WorkflowTestData fixture, bool findFromRows = true)
    {
        if (findFromRows)
            fixture.State.Setup(x => x.FindFailedJobsOfRunningRunsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int limit, CancellationToken _) => (IReadOnlyList<FailedMediaJob>)fixture.JobRows
                    .Where(job => job.Status == "failed" && (job.Kind is "render" or "export_zip") &&
                        fixture.RunRows.Single(run => run.Id == job.WorkflowRunId).Status == "running")
                    .Select(job => new FailedMediaJob(job.WorkflowRunId, job.WorkflowNodeRunId, job.ErrorMessage))
                    .Take(limit).ToArray());
        return new(fixture.State.Object, fixture.Unit.Object, fixture.Runtime);
    }
}
