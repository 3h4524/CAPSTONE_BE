using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class AiClipPhaseServiceTests
{
    private readonly WorkflowTestData fixture = new();
    private readonly Mock<IAiShotJobService> shots = new();
    private readonly Mock<ISceneVideoNotifier> sceneNotifier = new();
    private readonly Mock<IWorkflowRunNotifier> runNotifier = new();
    private readonly AiClipPhaseSettings settings = new();
    private WorkflowRun run = null!;
    private MediaJob job = null!;

    private static DateTime Now => WorkflowTestData.Now.UtcDateTime;

    private AiClipPhaseService Service(params AiShotJobState[] scenes)
    {
        var product = fixture.AddProduct();
        run = fixture.AddRun(product);
        job = fixture.AddJob(run);
        job.Status = AiClipPhase.JobStatus;
        job.LeaseToken = null;
        job.LeaseExpiresAt = null;
        job.Attempt = 0;
        job.Progress = AiClipPhase.StartProgress;
        var board = new Storyboard("auto", 2, 8, "tshirt", [], "board", Mode: "ai_shot");
        job.Payload = WorkflowJson.Write(new AiRenderJobPayload(Guid.NewGuid(), board, scenes));
        // What a failed generate-video step points at; the run's video is looked up by it.
        fixture.NodeRows.Single(x => x.Id == job.WorkflowNodeRunId).OutputSnapshot = WorkflowJson.Write(new VideoPointer(Guid.NewGuid()));
        fixture.State.Setup(x => x.FindClipPhaseJobsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => fixture.JobRows.Where(x => x.Status == AiClipPhase.JobStatus && x.Kind == "render")
                .Select(x => new ClipPhaseJob(x.Id, x.WorkflowRunId, x.UserId, x.Payload,
                    fixture.RunRows.SingleOrDefault(r => r.Id == x.WorkflowRunId)?.Status)).ToList());
        return new(fixture.State.Object, fixture.Unit.Object, shots.Object, fixture.Jobs.Object, fixture.Nodes.Object, fixture.Runtime,
            Options.Create(settings), fixture.Time, sceneNotifier.Object, runNotifier.Object);
    }

    private static AiShotJobState Scene(int order, string status, DateTime? submittedAt = null, string? failure = null) =>
        new(order, $"f{order}", status == "pending" ? null : $"op{order}", Status: status, SubmittedAt: submittedAt, Failure: failure);

    private IReadOnlyList<AiShotJobState> Scenes() => WorkflowJson.Read<AiRenderJobPayload>(job.Payload).AiShotStates;

    private void SceneStored(int order) =>
        job.Payload = WorkflowJson.Write(WorkflowJson.Read<AiRenderJobPayload>(job.Payload) with
        {
            AiShotStates = Scenes().Select(x => x.SceneOrder == order ? x with { Status = "stored", StorageKey = "k", StorageVersion = "1" } : x).ToArray()
        });

    private static Result<AiShotProgressResponse> Progress(string status) => Result.Success(new AiShotProgressResponse(status));
    private static Result<AiShotProgressResponse> Refused(string code) => Result.Failure<AiShotProgressResponse>(Error.Failure(code, "Refused."));

    [TestMethod]
    public async Task PlanAsync_NoJobMakingItsClips_HasNothingToDo()
    {
        var service = Service();
        fixture.JobRows.Clear();

        var plan = await service.PlanAsync(default);

        plan.HasJobs.Should().BeFalse();
        plan.Work.Should().BeEmpty();
    }

    [TestMethod]
    public async Task PlanAsync_ScenesStillBeingMade_ReturnsTheWorkAndLeavesTheJobAlone()
    {
        var service = Service(Scene(0, "polling", Now.AddMinutes(-1)), Scene(1, "pending"));

        var plan = await service.PlanAsync(default);

        plan.HasJobs.Should().BeTrue();
        plan.Work.Should().BeEquivalentTo([new AiClipWork(job.Id, 0, AiClipAction.Poll), new AiClipWork(job.Id, 1, AiClipAction.Submit)]);
        job.Status.Should().Be(AiClipPhase.JobStatus);
    }

    [TestMethod]
    public async Task PlanAsync_RunWasCancelled_CancelsTheJobSoNobodyLooksAtItAgain()
    {
        var service = Service(Scene(0, "polling", Now.AddMinutes(-1)));
        run.Status = "cancelled";

        var plan = await service.PlanAsync(default);

        job.Status.Should().Be("cancelled");
        plan.Work.Should().BeEmpty();
    }

    [TestMethod]
    public async Task PlanAsync_EveryClipStored_QueuesTheJobForTheWorker()
    {
        var service = Service(Scene(0, "stored"), Scene(1, "stored"));
        job.AvailableAt = Now.AddHours(-1);

        await service.PlanAsync(default);

        job.Status.Should().Be("queued");
        job.AvailableAt.Should().Be(Now);
        job.Progress.Should().Be(AiClipPhase.DoneProgress);
        var node = fixture.NodeRows.Single(x => x.Id == job.WorkflowNodeRunId);
        node.Progress.Should().Be(AiClipPhase.DoneProgress);
        node.Stage.Should().Be(AiClipPhase.Stage);
        run.Status.Should().Be("running");
        sceneNotifier.Verify(x => x.ChangedAsync(run.Id, It.IsAny<CancellationToken>()), Times.Once);
        runNotifier.Verify(x => x.ProgressAsync(run.Id, run.RunGroupId, AiClipPhase.Stage, AiClipPhase.DoneProgress, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PlanAsync_SceneRefusedByTheProvider_FailsTheJobAndTheRunWithItsReason()
    {
        var service = Service(Scene(0, "stored"), Scene(1, "failed", failure: "The image shows a minor."));

        var plan = await service.PlanAsync(default);

        plan.Work.Should().BeEmpty();
        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Contain("The image shows a minor.");
        run.Status.Should().Be("failed");
        run.ErrorMessage.Should().Be(job.ErrorMessage);
        fixture.NodeRows.Single(x => x.Id == job.WorkflowNodeRunId).Status.Should().Be("failed");
        runNotifier.Verify(x => x.RunChangedAsync(run, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task PlanAsync_SceneLeftSubmitting_FailsTheJobAsUncertain()
    {
        var service = Service(Scene(0, "submitting", failure: "MiniMax did not answer in time."));

        await service.PlanAsync(default);

        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Contain("uncertain").And.Contain("MiniMax did not answer in time.");
    }

    [TestMethod]
    public async Task PlanAsync_SceneNeverFinished_FailsTheJob()
    {
        var service = Service(Scene(0, "polling", Now.AddMinutes(-26)));

        await service.PlanAsync(default);

        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Be("AI Shot could not finish. Review the approved source or prompt, then regenerate AI clips if needed.");
    }

    [TestMethod]
    public async Task PlanAsync_PayloadCannotBeRead_FailsTheJobInsteadOfLoopingOnIt()
    {
        var service = Service(Scene(0, "pending"));
        job.Payload = "not json";

        var plan = await service.PlanAsync(default);

        plan.Work.Should().BeEmpty();
        job.Status.Should().Be("failed");
    }

    [TestMethod]
    public async Task RunAsync_Submit_AsksTheServiceToSubmitTheScene()
    {
        var service = Service(Scene(0, "pending"));
        shots.Setup(x => x.StartClipAsync(job.Id, 0, It.IsAny<CancellationToken>())).ReturnsAsync(Progress("polling"));

        await service.RunAsync(new(job.Id, 0, AiClipAction.Submit), default);

        shots.Verify(x => x.StartClipAsync(job.Id, 0, It.IsAny<CancellationToken>()), Times.Once);
        job.Status.Should().Be(AiClipPhase.JobStatus);
    }

    [TestMethod]
    [DataRow("VeoBusy")]
    [DataRow("AiVideoProviderBusy")]
    [DataRow("AiVideoNotSubmitted")]
    [DataRow("LeaseLost")]
    public async Task RunAsync_Submit_ProviderTurnedItAwayForNow_LeavesTheJobWaiting(string code)
    {
        var service = Service(Scene(0, "pending"));
        shots.Setup(x => x.StartClipAsync(job.Id, 0, It.IsAny<CancellationToken>())).ReturnsAsync(Refused(code));

        await service.RunAsync(new(job.Id, 0, AiClipAction.Submit), default);

        job.Status.Should().Be(AiClipPhase.JobStatus);
        run.Status.Should().Be("running");
    }

    [TestMethod]
    public async Task RunAsync_Submit_OutcomeUncertain_FailsTheJobSoItIsNeverSentAgain()
    {
        var service = Service(Scene(0, "submitting"));
        shots.Setup(x => x.StartClipAsync(job.Id, 0, It.IsAny<CancellationToken>())).ReturnsAsync(Refused("AiVideoSubmissionUncertain"));

        await service.RunAsync(new(job.Id, 0, AiClipAction.Submit), default);

        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Contain("Retry will not submit it again");
    }

    [TestMethod]
    public async Task RunAsync_Submit_DailyLimitUsedUp_FailsTheJobWithTheLimitsOwnWords()
    {
        var service = Service(Scene(0, "pending", failure: "MiniMax has made as many clips as the platform allows today. Try again tomorrow."));
        shots.Setup(x => x.StartClipAsync(job.Id, 0, It.IsAny<CancellationToken>())).ReturnsAsync(Refused("AiVideoDailyLimitReached"));

        await service.RunAsync(new(job.Id, 0, AiClipAction.Submit), default);

        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Contain("as many clips as the platform allows today").And.Contain("Nothing was billed");
    }

    [TestMethod]
    public async Task RunAsync_Submit_SourceChanged_FailsTheJob()
    {
        var service = Service(Scene(0, "pending"));
        shots.Setup(x => x.StartClipAsync(job.Id, 0, It.IsAny<CancellationToken>())).ReturnsAsync(Refused("SourceChanged"));

        await service.RunAsync(new(job.Id, 0, AiClipAction.Submit), default);

        job.Status.Should().Be("failed");
        job.ErrorMessage.Should().Contain("mock-up changed");
    }

    [TestMethod]
    public async Task RunAsync_Poll_ClipReady_IsStoredWithoutWaitingForTheNextPass()
    {
        var service = Service(Scene(0, "polling", Now.AddMinutes(-1)));
        shots.Setup(x => x.PollClipAsync(job.Id, 0, It.IsAny<CancellationToken>())).ReturnsAsync(Progress("ready"));
        shots.Setup(x => x.StoreClipAsync(job.Id, 0, It.IsAny<CancellationToken>()))
            .Callback(() => SceneStored(0)).ReturnsAsync(Result.Success());

        await service.RunAsync(new(job.Id, 0, AiClipAction.Poll), default);

        shots.Verify(x => x.StoreClipAsync(job.Id, 0, It.IsAny<CancellationToken>()), Times.Once);
        job.Status.Should().Be("queued", "the only scene is stored, so the worker can render");
    }

    [TestMethod]
    public async Task RunAsync_Poll_StillMaking_DoesNothingMore()
    {
        var service = Service(Scene(0, "polling", Now.AddMinutes(-1)));
        shots.Setup(x => x.PollClipAsync(job.Id, 0, It.IsAny<CancellationToken>())).ReturnsAsync(Progress("polling"));

        await service.RunAsync(new(job.Id, 0, AiClipAction.Poll), default);

        shots.Verify(x => x.StoreClipAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        job.Status.Should().Be(AiClipPhase.JobStatus);
    }

    [TestMethod]
    public async Task RunAsync_Poll_ProviderUnreachable_TriesAgainOnTheNextPass()
    {
        var service = Service(Scene(0, "polling", Now.AddMinutes(-1)));
        shots.Setup(x => x.PollClipAsync(job.Id, 0, It.IsAny<CancellationToken>())).ReturnsAsync(Refused("AiVideoProviderUnavailable"));

        await service.RunAsync(new(job.Id, 0, AiClipAction.Poll), default);

        job.Status.Should().Be(AiClipPhase.JobStatus);
    }

    [TestMethod]
    public async Task RunAsync_Poll_NoOperationWasRecorded_FailsTheJobAsUncertain()
    {
        var service = Service(Scene(0, "polling", Now.AddMinutes(-1)));
        shots.Setup(x => x.PollClipAsync(job.Id, 0, It.IsAny<CancellationToken>())).ReturnsAsync(Refused("AiVideoSubmissionUncertain"));

        await service.RunAsync(new(job.Id, 0, AiClipAction.Poll), default);

        job.Status.Should().Be("failed");
    }

    [TestMethod]
    public async Task RunAsync_Store_OneOfTwoScenesStored_MovesTheProgressOnAndKeepsMakingTheRest()
    {
        var service = Service(Scene(0, ""), Scene(1, "polling", Now.AddMinutes(-1)));
        SceneStored(0);
        shots.Setup(x => x.StoreClipAsync(job.Id, 0, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        await service.RunAsync(new(job.Id, 0, AiClipAction.Store), default);

        job.Status.Should().Be(AiClipPhase.JobStatus);
        job.Progress.Should().Be(AiClipPhase.StartProgress + 10);
        fixture.NodeRows.Single(x => x.Id == job.WorkflowNodeRunId).Progress.Should().Be(AiClipPhase.StartProgress + 10);
        runNotifier.Verify(x => x.ProgressAsync(run.Id, run.RunGroupId, AiClipPhase.Stage, AiClipPhase.StartProgress + 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RunAsync_Store_LastSceneStored_QueuesTheJobOnce()
    {
        var service = Service(Scene(0, "stored"), Scene(1, "ready", Now.AddMinutes(-1)));
        shots.Setup(x => x.StoreClipAsync(job.Id, 1, It.IsAny<CancellationToken>()))
            .Callback(() => SceneStored(1)).ReturnsAsync(Result.Success());

        await service.RunAsync(new(job.Id, 1, AiClipAction.Store), default);
        // A second finishing call for the same job finds it already handed on.
        await service.RunAsync(new(job.Id, 1, AiClipAction.Store), default);

        job.Status.Should().Be("queued");
        fixture.Transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RunAsync_Store_ClipCouldNotBeStored_KeepsTheJobWaiting()
    {
        var service = Service(Scene(0, "ready", Now.AddMinutes(-1)));
        shots.Setup(x => x.StoreClipAsync(job.Id, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Failure("AiVideoStoreFailed", "Could not store.")));

        await service.RunAsync(new(job.Id, 0, AiClipAction.Store), default);

        job.Status.Should().Be(AiClipPhase.JobStatus);
        fixture.Transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
