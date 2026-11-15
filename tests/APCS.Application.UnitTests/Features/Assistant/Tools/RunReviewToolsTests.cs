using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Assistant;
using APCS.Application.Features.Assistant.Common;
using APCS.Application.Features.Assistant.Dtos.Request;
using APCS.Application.Features.Assistant.Dtos.Response;
using APCS.Application.Features.Assistant.Tools;
using APCS.Application.Features.Assistant.Tools.ClientActions;
using APCS.Application.Features.Assistant.Tools.Confirmations;
using APCS.Application.Features.DesignGeneration;
using APCS.Application.Features.DesignGeneration.Dtos.Request;
using APCS.Application.Features.DesignGeneration.Dtos.Response;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace APCS.Application.UnitTests.Features.Assistant.Tools;

[TestClass]
public sealed class RunReviewToolsTests
{
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();

    private readonly Mock<IAssistantRunProgressReader> progress = new();
    private readonly Mock<IDesignGenerationService> jobs = new();
    private readonly Mock<IWorkflowRunGroupService> groups = new();
    private readonly Mock<IWorkflowRunService> runs = new();
    private readonly Mock<IMockupAssetService> mockups = new();
    private readonly Mock<IAssistantPendingActionRepository> pendingActions = new();
    private readonly List<AssistantPendingAction> added = [];

    public RunReviewToolsTests() =>
        pendingActions.Setup(r => r.AddAsync(It.IsAny<AssistantPendingAction>(), true, It.IsAny<CancellationToken>()))
            .Callback<AssistantPendingAction, bool, CancellationToken>((action, _, _) => added.Add(action)).Returns(Task.CompletedTask);

    private static AssistantToolContext Context(bool withCanvas = true)
    {
        var canvas = withCanvas ? new AssistantCanvasDto(WorkflowId, 2, false, [], [], "Etsy flow") : null;
        return new(Guid.NewGuid(), AssistantTestSupport.UserId, AssistantTestSupport.Seller,
            new AssistantClientContextDto("/workflows", WorkflowId, null, null, canvas));
    }

    private void At(string stage, string state = AssistantRunStates.WaitingForUser, Guid? jobId = null, Guid? groupId = null, Guid? runId = null) =>
        progress.Setup(reader => reader.ReadAsync(WorkflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AssistantRunProgressResponseDto(WorkflowId, "Etsy flow", stage, state, "next",
                BatchJobId: jobId, RunGroupId: groupId, RunId: runId)));

    private ReviewDesignsTool DesignsTool() => new(progress.Object, jobs.Object, pendingActions.Object, new FakeTimeProvider());

    private ReviewMockupsTool MockupsTool() => new(progress.Object, groups.Object, runs.Object, mockups.Object, pendingActions.Object, new FakeTimeProvider());

    private static BatchJobProductResultDto Product(string name, string status, params (Guid Id, int Variation, string Approval)[] images) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, "tshirt", 0, status, null,
            images.Select(image => new GeneratedImageDto(image.Id, "u", image.Variation, 1, 1, image.Approval)).ToArray());

    private void WithJob(params BatchJobProductResultDto[] products) =>
        jobs.Setup(service => service.GetJobAsync(JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new BatchJobDetailDto(JobId, Guid.NewGuid(), "Batch", "completed", products.Length, products.Length, 0, 100,
                null, null, 2, "1:1", new BatchJobCountersDto(0, 0, products.Length, 0), products, RequireApproval: true, WorkflowId: WorkflowId)));

    private static T Stored<T>(AssistantPendingAction action) => JsonSerializer.Deserialize<T>(action.Arguments, AssistantJson.Options)!;

    [TestMethod]
    public async Task ReviewDesigns_ForEveryWaitingProduct_StoresNoIdsSoTheServiceApprovesThemAll()
    {
        At(AssistantRunStages.DesignApproval, jobId: JobId);
        WithJob(Product("Cat tee", "image_review_required", (Guid.NewGuid(), 0, "pending")), Product("Dog tee", "image_review_required", (Guid.NewGuid(), 0, "pending")));

        var outcome = await DesignsTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"decision":"approve","reply":"Confirm to approve."}"""), default);

        outcome.Succeeded.Should().BeTrue();
        added.Single().Title.Should().Be("Approve designs of 2 products");
        var stored = Stored<ReviewDesignsStoredArgs>(added.Single());
        stored.DesignImageIds.Should().BeNull();
        stored.Status.Should().Be("approved");
        stored.BatchJobId.Should().Be(JobId);
    }

    [TestMethod]
    public async Task ReviewDesigns_ForNamedProducts_ApprovesTheirFirstPendingDesign()
    {
        At(AssistantRunStages.DesignApproval, jobId: JobId);
        var first = Guid.NewGuid();
        WithJob(
            Product("Cat tee", "image_review_required", (Guid.NewGuid(), 1, "pending"), (first, 0, "pending")),
            Product("Dog tee", "image_review_required", (Guid.NewGuid(), 0, "pending")));

        await DesignsTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"decision":"approve","products":["cat"],"reply":"ok"}"""), default);

        Stored<ReviewDesignsStoredArgs>(added.Single()).DesignImageIds.Should().Equal(first);
    }

    [TestMethod]
    public async Task ReviewDesigns_WithAnUnknownProduct_ListsTheWaitingOnes()
    {
        At(AssistantRunStages.DesignApproval, jobId: JobId);
        WithJob(Product("Cat tee", "image_review_required", (Guid.NewGuid(), 0, "pending")));

        var outcome = await DesignsTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"decision":"reject","products":["mug"],"reply":"ok"}"""), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("Cat tee");
        added.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ReviewDesigns_WhenNothingWaits_SaysSo()
    {
        At(AssistantRunStages.Video, AssistantRunStates.Working);

        var outcome = await DesignsTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"decision":"approve","reply":"ok"}"""), default);

        outcome.ModelResultJson.Should().Contain("nothing_to_review");
    }

    [TestMethod]
    public async Task ReviewDesigns_OnConfirm_CallsTheApprovalService()
    {
        jobs.Setup(service => service.SetImageApprovalAsync(JobId, It.Is<SetImageApprovalRequestDto>(request => request.DesignImageIds == null && request.Status == "approved"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ImageApprovalResultDto(2, 0, 2, 0)));

        var result = await DesignsTool().ExecuteConfirmedAsync(Guid.NewGuid(), AssistantJson.SerializeToElement(new ReviewDesignsStoredArgs(WorkflowId, JobId, null, "approved")), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Summary.Should().Contain("2 approved");
    }

    private static RunGroupTargetResponse Target(string design, string product, Guid productId, string group) => new(
        "k", productId, product, "tshirt", group, design, null, 2, 0, 2, true, false, [], [], null, null, null, null, null, 0, null);

    private static MockupResponse Mockup(Guid productId, string? group, string status, long revision = 3) => new(
        Guid.NewGuid(), productId, "generated", "Hero", group, null, revision, status, null, 1, 1, new MockupRegions(), "u", []);

    [TestMethod]
    public async Task ReviewMockups_ForAGroup_TakesThePendingMockupsOfTheNamedDesignOnly()
    {
        var productId = Guid.NewGuid();
        At(AssistantRunStages.MockupApproval, groupId: GroupId);
        groups.Setup(service => service.GetAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RunGroupResponse(GroupId, WorkflowId, Guid.NewGuid(), null, "waiting_for_targets", 4, 2, DateTimeOffset.UnixEpoch, 10, 2,
                new RunGroupProgress(0, 0, 0, 0, 0, 0, 0, 0), [Target("Design 1", "Cat tee", productId, "g1"), Target("Design 2", "Cat tee", productId, "g2")])));
        var wanted = Mockup(productId, "g1", "pending", 7);
        mockups.Setup(service => service.ListAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MockupResponse>>([wanted, Mockup(productId, "g1", "approved"), Mockup(productId, "g2", "pending")]));

        var outcome = await MockupsTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"decision":"approve","designs":["design 1"],"reply":"ok"}"""), default);

        outcome.Succeeded.Should().BeTrue();
        added.Single().Title.Should().Be("Approve 1 mock-up");
        Stored<ReviewMockupsStoredArgs>(added.Single()).Mockups.Should().Equal(new ReviewMockupItem(wanted.Id, 7));
    }

    [TestMethod]
    public async Task ReviewMockups_ForASingleRun_TakesEveryPendingMockupOfItsProduct()
    {
        var runId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        At(AssistantRunStages.MockupApproval, runId: runId);
        runs.Setup(service => service.GetAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RunResponse(runId, WorkflowId, productId, "waiting_for_input", 1, 1, DateTimeOffset.UnixEpoch, null, [])));
        mockups.Setup(service => service.ListAsync(productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MockupResponse>>([Mockup(productId, "a", "pending"), Mockup(productId, "b", "pending")]));

        await MockupsTool().ExecuteAsync(Context(), AssistantTestSupport.Json("""{"decision":"reject","reply":"ok"}"""), default);

        var stored = Stored<ReviewMockupsStoredArgs>(added.Single());
        stored.Approved.Should().BeFalse();
        stored.Mockups.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task ReviewMockups_OnConfirm_ReviewsEachAndCountsTheOnesThatChanged()
    {
        var ok = Guid.NewGuid();
        var stale = Guid.NewGuid();
        mockups.Setup(service => service.ReviewAsync(ok, It.Is<MockupReviewRequest>(request => request.Approved && request.ExpectedRevision == 3), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Mockup(Guid.NewGuid(), null, "approved")));
        mockups.Setup(service => service.ReviewAsync(stale, It.IsAny<MockupReviewRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MockupResponse>(Error.Conflict("StaleRevision", "Mockup changed.")));

        var result = await MockupsTool().ExecuteConfirmedAsync(Guid.NewGuid(),
            AssistantJson.SerializeToElement(new ReviewMockupsStoredArgs(WorkflowId, true, [new(ok, 3), new(stale, 3)])), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Summary.Should().Be("1 mock-ups approved; 1 changed in the meantime and were left for review.");
    }

    [TestMethod]
    public async Task ContinueRun_AtMockupApproval_OffersTheContinueButton()
    {
        At(AssistantRunStages.MockupApproval, groupId: GroupId);

        var outcome = await new ContinueRunTool(progress.Object).ExecuteAsync(Context(), AssistantTestSupport.Json("""{"reply":"Press Continue."}"""), default);

        outcome.Succeeded.Should().BeTrue();
        outcome.EndsTurn.Should().BeTrue();
        var action = outcome.Events.Single().Data.Should().BeOfType<AssistantClientActionData>().Subject;
        action.Kind.Should().Be(ContinueRunTool.ActionKind);
        action.Payload.GetProperty("stage").GetString().Should().Be(AssistantRunStages.MockupApproval);
        action.Payload.GetProperty("label").GetString().Should().Be("Continue to the videos");
        action.Payload.GetProperty("runGroupId").GetGuid().Should().Be(GroupId);
    }

    [TestMethod]
    public async Task ContinueRun_WhileTheRunWorks_ReturnsAnError()
    {
        At(AssistantRunStages.Video, AssistantRunStates.Working);

        var outcome = await new ContinueRunTool(progress.Object).ExecuteAsync(Context(), AssistantTestSupport.Json("""{"reply":"ok"}"""), default);

        outcome.Succeeded.Should().BeFalse();
        outcome.ModelResultJson.Should().Contain("nothing_to_continue");
    }

    [TestMethod]
    public void ContinueRun_NeedsTheWorkflowOpen() =>
        new ContinueRunTool(progress.Object).IsAvailable(Context(withCanvas: false)).Should().BeFalse();
}
