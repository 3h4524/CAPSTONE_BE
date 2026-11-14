using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.Workflows;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowRunGroupServiceTests
{
    [TestMethod]
    public async Task StartAsync_StandardScenes_WaitsForTheDesignsToBeChosen()
    {
        var data = new RunGroupTestData();
        var workflow = data.AddWorkflow();

        var result = await data.Service.StartAsync(workflow.Id, new(4, "group-1"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("waiting_for_targets");
        data.GroupRows.Should().ContainSingle().Which.BatchId.Should().Be(data.BatchId);
        data.Base.RunRows.Should().BeEmpty();
        data.Entitlements.AllowancesRequested.Should().Contain((UsageAllowances.FullRuns, 1));
    }

    [TestMethod]
    public async Task StartAsync_SameKeyTwice_ReturnsTheSameGroup()
    {
        var data = new RunGroupTestData();
        var workflow = data.AddWorkflow();
        var first = await data.Service.StartAsync(workflow.Id, new(4, "same"), default);
        var second = await data.Service.StartAsync(workflow.Id, new(4, "same"), default);
        second.Value.Id.Should().Be(first.Value.Id);
        data.GroupRows.Should().ContainSingle();
    }

    [TestMethod]
    public async Task StartAsync_AiScenes_MakeAVideoForEveryDesign()
    {
        var data = new RunGroupTestData();
        var workflow = data.AddWorkflow(data.AiConfig());

        var result = await data.Service.StartAsync(workflow.Id, new(4, "ai"), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        data.GroupRows.Should().ContainSingle();
    }


    [TestMethod]
    public async Task GetAsync_WaitingGroup_ListsEveryDesignWithItsScenesResolved()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var other = data.AddProduct("Birth Month Blooms");
        var (design, mockups) = data.AddDesign(other, 1, approved: true);
        data.AddDesign(other, 2, approved: false);

        var result = (await data.Service.GetAsync(group.Id, default)).Value;

        result.Targets.Should().HaveCount(3);
        var target = result.Targets.Single(x => x.ArtworkGroupKey == design.Id.ToString());
        target.DesignLabel.Should().Be("Design 1");
        target.Selected.Should().BeTrue();
        target.Scenes.Select(x => x.MockupId).Should().Equal(mockups[0].Id, mockups[1].Id);
        result.Targets.Single(x => x.ProductId == other.Id && x.DesignLabel == "Design 2").Selected.Should().BeFalse();
        result.SelectedCount.Should().Be(2);
    }

    [TestMethod]
    public async Task GetAsync_ProductWithAnApprovedDesign_ListsOnlyThatDesignsMockups()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var other = data.AddProduct("Birth Month Blooms");
        var (earlier, _) = data.AddDesign(other, 1, approved: true);
        var (chosen, _) = data.AddDesign(other, 2, approved: false);
        earlier.ApprovalStatus = "rejected";

        var result = (await data.Service.GetAsync(group.Id, default)).Value;

        result.Targets.Where(x => x.ProductId == other.Id).Should().ContainSingle()
            .Which.ArtworkGroupKey.Should().Be(chosen.Id.ToString());
    }

    [TestMethod]
    public async Task ConfirmAsync_ApprovedDesigns_CreatesOneQueuedRunEachPastMockupApproval()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var other = data.AddProduct("Birth Month Blooms");
        var (design, mockups) = data.AddDesign(other, 0, approved: true);

        var result = await data.Service.ConfirmAsync(group.Id, new(1), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("running");
        data.Base.RunRows.Should().HaveCount(2).And.OnlyContain(run => run.Status == "queued" && run.RunGroupId == group.Id);
        var run = data.Base.RunRows.Single(x => x.ProductId == other.Id);
        run.ArtworkGroupKey.Should().Be(design.Id.ToString());
        var steps = data.Base.NodeRows.Where(x => x.WorkflowRunId == run.Id).ToArray();
        steps.Single(x => x.NodeType == "approval-gate").Status.Should().Be("succeeded");
        steps.Single(x => x.NodeType == "generate-video").Status.Should().Be("pending");
        var generate = WorkflowJson.Read<GenerateVideoConfig>(steps.Single(x => x.NodeType == "generate-video").InputSnapshot);
        generate.AiScenes!.Select(x => x.MockupId).Should().Equal(mockups[0].Id, mockups[1].Id);
        var definition = WorkflowJson.Read<WorkflowDefinition>(run.DefinitionSnapshot);
        WorkflowJson.Config<ProductInputConfig>(definition.Nodes.Single(x => x.Type == "product-input")).ProductId.Should().Be(other.Id);
        WorkflowJson.Config<MockupConfig>(definition.Nodes.Single(x => x.Type == "apply-mockup")).ArtworkGroupKey.Should().Be(design.Id.ToString());
        data.Entitlements.AllowancesRequested.Should().Contain((UsageAllowances.Videos, 2));
        data.Planner.Verify(x => x.Signal(), Times.Once);
        data.Notifier.Verify(x => x.GroupChangedAsync(group.Id, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task ConfirmAsync_ScenesSavedWhileWaiting_AreTheOnesUsed()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var workflow = data.Base.WorkflowRows.Single(x => x.Id == group.WorkflowId);
        var changed = data.SharedConfig() with { DurationSeconds = 5, AiScenes = [data.SharedConfig().AiScenes![1]] };
        workflow.Definition = WorkflowJson.Write(WorkflowTestData.Definition(data.Preview, changed));
        workflow.Revision = 5;

        (await data.Service.ConfirmAsync(group.Id, new(1), default)).IsSuccess.Should().BeTrue();

        var run = data.Base.RunRows.Single();
        var generate = WorkflowJson.Read<GenerateVideoConfig>(data.Base.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video").InputSnapshot);
        generate.AiScenes.Should().ContainSingle().Which.MockupId.Should().Be(data.PreviewMockups[1].Id);
    }

    [TestMethod]
    public async Task ConfirmAsync_AiVideos_ConfirmWhatEveryDesignCostsTogether()
    {
        var data = new RunGroupTestData();
        var workflow = data.AddWorkflow(data.AiConfig());
        var group = (await data.Service.StartAsync(workflow.Id, new(4, "ai"), default)).Value;
        data.AddDesign(data.AddProduct("Birth Month Blooms"), 0, approved: true);

        var result = await data.Service.ConfirmAsync(group.Id, new(1, "quote-token"), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        data.Base.Quotes.Verify(x => x.VerifyBatchAsync(group.Id,
            It.Is<IReadOnlyList<PlannedVideo>>(plans => plans.Count == 2 && plans.All(plan => plan.Config.Mode == "ai_shot")),
            "quote-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ConfirmAsync_AiCostNotConfirmed_MakesNoVideo()
    {
        var data = new RunGroupTestData();
        var workflow = data.AddWorkflow(data.AiConfig());
        var group = (await data.Service.StartAsync(workflow.Id, new(4, "ai"), default)).Value;
        data.Base.Quotes.Setup(x => x.VerifyBatchAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<PlannedVideo>>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(Error.Validation("Confirm what the AI videos of this run cost before starting them.")));

        var result = await data.Service.ConfirmAsync(group.Id, new(1), default);

        result.IsFailure.Should().BeTrue();
        data.Base.RunRows.Should().BeEmpty();
        data.GroupRows.Single().Status.Should().Be("waiting_for_targets");
    }

    [TestMethod]
    public async Task QuoteAiAsync_WaitingGroup_QuotesEveryDesignWithTheRunItWouldGet()
    {
        var data = new RunGroupTestData();
        var workflow = data.AddWorkflow(data.AiConfig());
        var group = (await data.Service.StartAsync(workflow.Id, new(4, "ai"), default)).Value;
        data.Base.Quotes.Setup(x => x.QuoteBatchAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<PlannedVideo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AiShotQuoteResponse("token", 1.2m, 2, WorkflowTestData.Now.AddMinutes(10), "MiniMax-H3-Lite", "Estimate")));

        var result = await data.Service.QuoteAiAsync(group.Id, new(1), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.EstimatedCostUsd.Should().Be(1.2m);
        data.Base.Quotes.Verify(x => x.QuoteBatchAsync(group.Id, It.Is<IReadOnlyList<PlannedVideo>>(plans =>
            plans.Single().Run.ProductId == data.Preview.Id && plans.Single().Label.Contains("DAVIES")), It.IsAny<CancellationToken>()), Times.Once);
        data.Base.RunRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ConfirmAsync_NoApprovedDesign_WaitsForApproval()
    {
        var data = new RunGroupTestData(previewApproved: false);
        var group = await data.StartAsync();
        var result = await data.Service.ConfirmAsync(group.Id, new(1), default);
        result.Error.Code.Should().Be("WaitingForInput");
        data.Base.RunRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ConfirmAsync_MoreDesignsThanTheLimit_IsRefused()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var product = data.AddProduct("Many designs");
        for (var index = 0; index < WorkflowRunGroupService.MaximumTargets; index++) data.AddDesign(product, index, approved: true);

        var result = await data.Service.ConfirmAsync(group.Id, new(1), default);

        result.Error.Message.Should().Contain($"at most {WorkflowRunGroupService.MaximumTargets}");
        data.Base.RunRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ConfirmAsync_VideosUsedUp_CreatesNoRun()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        data.Entitlements.AllowancesUsedUp.Add(UsageAllowances.Videos);

        var result = await data.Service.ConfirmAsync(group.Id, new(1), default);

        result.IsFailure.Should().BeTrue();
        data.Base.RunRows.Should().BeEmpty();
        data.GroupRows.Single().Status.Should().Be("waiting_for_targets");
    }

    [TestMethod]
    public async Task ConfirmAsync_StaleRevision_IsRefused()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        (await data.Service.ConfirmAsync(group.Id, new(9), default)).Error.Code.Should().Be("StaleRevision");
    }

    [TestMethod]
    public async Task UpdateOverrideAsync_PickedImage_IsUsedByThatDesignsRun()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var picked = data.PreviewMockups[1];

        var updated = await data.Service.UpdateOverrideAsync(group.Id,
            new(1, data.Preview.Id, data.PreviewDesign.Id.ToString(), [new SceneMockupOverride(0, picked.Id)]), default);
        updated.Value.Targets.Single().HasOverride.Should().BeTrue();
        updated.Value.Targets.Single().Scenes[0].Overridden.Should().BeTrue();

        (await data.Service.ConfirmAsync(group.Id, new(2), default)).IsSuccess.Should().BeTrue();
        var run = data.Base.RunRows.Single();
        var generate = WorkflowJson.Read<GenerateVideoConfig>(data.Base.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video").InputSnapshot);
        generate.AiScenes![0].MockupId.Should().Be(picked.Id);
    }

    [TestMethod]
    public async Task UpdateOverrideAsync_ImageOfAnotherDesign_IsRefused()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var other = data.AddProduct("Other");
        var (_, foreign) = data.AddDesign(other, 0, approved: true);

        var result = await data.Service.UpdateOverrideAsync(group.Id,
            new(1, data.Preview.Id, data.PreviewDesign.Id.ToString(), [new SceneMockupOverride(0, foreign[0].Id)]), default);

        result.IsFailure.Should().BeTrue();
        data.GroupRows.Single().TargetOverrides.Should().Be("{}");
    }

    [TestMethod]
    public async Task UpdateConfigAsync_OwnSettings_AreTheOnesThatDesignsRunUses()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var own = data.SharedConfig() with { DurationSeconds = 6, OutputFormat = "square", AiScenes = [data.SharedConfig().AiScenes![1] with { DurationSeconds = 6 }] };

        var updated = await data.Service.UpdateConfigAsync(group.Id, new(1, data.Preview.Id, data.PreviewDesign.Id.ToString(), own), default);

        var target = updated.Value.Targets.Single();
        target.HasOwnConfig.Should().BeTrue();
        target.Config!.DurationSeconds.Should().Be(6);
        target.Scenes.Should().ContainSingle().Which.MockupId.Should().Be(data.PreviewMockups[1].Id);
        (await data.Service.ConfirmAsync(group.Id, new(2), default)).IsSuccess.Should().BeTrue();
        var run = data.Base.RunRows.Single();
        var generate = WorkflowJson.Read<GenerateVideoConfig>(data.Base.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video").InputSnapshot);
        generate.DurationSeconds.Should().Be(6);
        generate.OutputFormat.Should().Be("square");
        generate.AiScenes.Should().ContainSingle().Which.MockupId.Should().Be(data.PreviewMockups[1].Id);
    }

    [TestMethod]
    public async Task UpdateConfigAsync_Null_PutsTheDesignBackOnTheSharedSettingsAndKeepsItsScenePicks()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var key = data.PreviewDesign.Id.ToString();
        await data.Service.UpdateOverrideAsync(group.Id, new(1, data.Preview.Id, key, [new SceneMockupOverride(0, data.PreviewMockups[1].Id)]), default);
        await data.Service.UpdateConfigAsync(group.Id, new(2, data.Preview.Id, key, data.SharedConfig() with { DurationSeconds = 6, AiScenes = [data.SharedConfig().AiScenes![0] with { DurationSeconds = 6 }] }), default);

        var reset = await data.Service.UpdateConfigAsync(group.Id, new(3, data.Preview.Id, key, null), default);

        var target = reset.Value.Targets.Single();
        target.HasOwnConfig.Should().BeFalse();
        target.Config.Should().BeNull();
        target.HasOverride.Should().BeTrue();
        target.Scenes.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task UpdateOverrideAsync_ScenePick_KeepsTheDesignsOwnSettings()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var key = data.PreviewDesign.Id.ToString();
        await data.Service.UpdateConfigAsync(group.Id, new(1, data.Preview.Id, key, data.SharedConfig() with { DurationSeconds = 7 }), default);

        var updated = await data.Service.UpdateOverrideAsync(group.Id, new(2, data.Preview.Id, key, [new SceneMockupOverride(0, data.PreviewMockups[1].Id)]), default);

        updated.Value.Targets.Single().Config!.DurationSeconds.Should().Be(7);
    }

    [TestMethod]
    public async Task UpdateConfigAsync_AiScene_IsStoredForTheDesign()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();

        var result = await data.Service.UpdateConfigAsync(group.Id, new(1, data.Preview.Id, data.PreviewDesign.Id.ToString(), data.AiConfig()), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Targets.Single().Config!.Mode.Should().Be("ai_shot");
    }

    [TestMethod]
    public async Task UpdateConfigAsync_SettingsOfOneProductOnly_AreRefused()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var pack = data.AiConfig() with { Template = "product_on_model_pack" };

        var result = await data.Service.UpdateConfigAsync(group.Id, new(1, data.Preview.Id, data.PreviewDesign.Id.ToString(), pack), default);

        result.IsFailure.Should().BeTrue();
        data.GroupRows.Single().TargetOverrides.Should().Be("{}");
    }

    [TestMethod]
    public async Task UpdateConfigAsync_AfterTheVideosStarted_IsRefused()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        await data.Service.ConfirmAsync(group.Id, new(1), default);

        var result = await data.Service.UpdateConfigAsync(group.Id, new(2, data.Preview.Id, data.PreviewDesign.Id.ToString(), data.SharedConfig()), default);

        result.Error.Code.Should().Be("TargetsConfirmed");
    }

    [TestMethod]
    public async Task GetAsync_StartedGroup_GivesEachDesignTheSettingsItsRunRendersWith()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        await data.Service.ConfirmAsync(group.Id, new(1), default);

        var started = await data.Service.GetAsync(group.Id, default);

        var target = started.Value.Targets.Single();
        target.Config.Should().NotBeNull();
        target.Config!.AiScenes!.Select(scene => scene.MockupId).Should().Equal(data.PreviewMockups[0].Id, data.PreviewMockups[1].Id);
        target.HasOwnConfig.Should().BeFalse();
    }

    [TestMethod]
    public async Task StartAsync_AutoMockupApproval_ApprovesTheWaitingMockupsAndStartsTheVideos()
    {
        var data = new RunGroupTestData(previewApproved: false);

        var group = await data.StartAsync(gateMode: "auto");

        group.Status.Should().Be("running");
        data.PreviewMockups.Should().OnlyContain(x => x.ApprovalStatus == "approved" && x.ApprovedRevision == x.MetadataRevision);
        data.Base.RunRows.Should().ContainSingle().Which.Status.Should().Be("queued");
    }

    [TestMethod]
    public async Task StartAsync_AutoMockupApproval_LeavesARejectedMockupRejected()
    {
        var data = new RunGroupTestData(previewApproved: false);
        data.PreviewMockups[1].ApprovalStatus = "rejected";

        var group = await data.StartAsync(gateMode: "auto");

        group.Status.Should().Be("running");
        data.PreviewMockups[0].ApprovalStatus.Should().Be("approved");
        data.PreviewMockups[1].ApprovalStatus.Should().Be("rejected");
    }

    [TestMethod]
    public async Task StartAsync_AutoMockupApprovalThatCannotStart_WaitsAtMockupApprovalWithTheMockupsApproved()
    {
        var data = new RunGroupTestData(previewApproved: false);
        for (var index = 0; index < WorkflowRunGroupService.MaximumTargets; index++) data.AddDesign(data.Preview, index + 2, approved: false);

        var group = await data.StartAsync(gateMode: "auto");

        group.Status.Should().Be("waiting_for_targets");
        data.Base.RunRows.Should().BeEmpty();
        data.PreviewMockups.Should().OnlyContain(x => x.ApprovalStatus == "approved");
        // The seller is told why nothing started, and it is kept for when the run is opened again.
        group.Notice.Should().Contain("at most");
        data.GroupRows.Single().ErrorMessage.Should().Be(group.Notice);
    }

    [TestMethod]
    public async Task StartAsync_AutoMockupApprovalThatStarts_HasNothingToSay()
    {
        var data = new RunGroupTestData(previewApproved: false);

        var group = await data.StartAsync(gateMode: "auto");

        group.Status.Should().Be("running");
        group.Notice.Should().BeNull();
    }

    [TestMethod]
    public async Task StartAsync_AutoApprovalLeavesOutTheDesignThatCannotBeMade_AndStartsTheOthers()
    {
        var data = new RunGroupTestData(previewApproved: false);
        data.AddSystemModel();
        data.ModelPhotosSucceed();
        data.QuotesCost(3.20m);
        // A mug has no model photo of the library to dress, so its scenes with a person cannot start.
        var mug = data.AddProduct("Mug");
        mug.ProductType = "mug";
        data.AddDesign(mug, 1, approved: false);
        var workflow = data.AddWorkflow(data.PersonConfig(), gateMode: "auto");

        var group = (await data.Service.StartAsync(workflow.Id, new(4, "left-out"), default)).Value;

        group.Status.Should().Be("running");
        data.Base.RunRows.Should().ContainSingle().Which.ProductId.Should().Be(data.Preview.Id);
        group.Notice.Should().StartWith("Left out of the run: Mug").And.Contain("model photo");
        group.Targets.Should().ContainSingle().Which.ProductName.Should().Be("DAVIES T-Shirt");
    }

    [TestMethod]
    public async Task StartAsync_AutoApprovalWhenNoDesignCanBeMade_WaitsWithEveryReason()
    {
        var data = new RunGroupTestData(previewApproved: false);
        var workflow = data.AddWorkflow(data.PersonConfig(), gateMode: "auto");

        var group = (await data.Service.StartAsync(workflow.Id, new(4, "none"), default)).Value;

        group.Status.Should().Be("waiting_for_targets");
        data.Base.RunRows.Should().BeEmpty();
        group.Notice.Should().Contain("DAVIES T-Shirt").And.Contain("model photo");
    }

    [TestMethod]
    public async Task StartAsync_ScenesWithAPersonInTheBackground_AnswersAtOnceAndMakesTheModelPhotosAfterwards()
    {
        var data = new RunGroupTestData(previewApproved: false, background: true);
        data.AddSystemModel();
        data.ModelPhotosSucceed();
        data.QuotesCost(3.20m);
        var workflow = data.AddWorkflow(data.PersonConfig(), gateMode: "auto");

        var group = (await data.Service.StartAsync(workflow.Id, new(4, "later"), default)).Value;

        // Pressing Run costs the seller no wait for the image provider: nothing is made yet, and the run says it is getting ready.
        group.Status.Should().Be("waiting_for_targets");
        group.Preparing.Should().BeTrue();
        group.Notice.Should().BeNull();
        data.Lifestyle.Verify(x => x.GenerateForVideoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        data.StartQueue!.Requests.Should().ContainSingle().Which.Should().Be(
            new RunGroupStartRequest(group.Id, WorkflowTestData.Owner, AutoApprove: true));

        await data.Service.PrepareAsync(group.Id, autoApprove: true, default);

        data.Lifestyle.Verify(x => x.GenerateForVideoAsync(data.Preview.Id, data.PreviewMockups[0].Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        data.Base.RunRows.Should().ContainSingle();
        data.GroupRows.Single().Status.Should().Be("running");
        data.StartQueue.IsPending(group.Id).Should().BeFalse();
        data.Notifier.Verify(x => x.GroupChangedAsync(group.Id, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task StartAsync_AutoApprovalInTheBackground_AnswersAtOnceAndStartsTheVideosAfterwards()
    {
        var data = new RunGroupTestData(previewApproved: false, background: true);
        data.QuotesCost(3.20m);
        var workflow = data.AddWorkflow(data.AiConfig(), gateMode: "auto");

        var group = (await data.Service.StartAsync(workflow.Id, new(4, "auto"), default)).Value;

        // Quoting the AI scenes plans every design's storyboard: Run does not wait for it.
        group.Status.Should().Be("waiting_for_targets");
        group.Preparing.Should().BeTrue();
        data.Base.Quotes.Verify(x => x.QuoteBatchAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<PlannedVideo>>(), It.IsAny<CancellationToken>()), Times.Never);

        await data.Service.PrepareAsync(group.Id, autoApprove: true, default);

        data.GroupRows.Single().Status.Should().Be("running");
        data.Base.RunRows.Should().ContainSingle();
    }

    [TestMethod]
    public async Task StartAsync_ManualApprovalWithoutAPerson_WaitsForTheSellerWithoutTheBackground()
    {
        var data = new RunGroupTestData(previewApproved: false, background: true);
        var workflow = data.AddWorkflow(data.AiConfig());

        var group = (await data.Service.StartAsync(workflow.Id, new(4, "manual"), default)).Value;

        group.Status.Should().Be("waiting_for_targets");
        group.Preparing.Should().BeFalse();
        data.StartQueue!.Requests.Should().BeEmpty();
    }

    [TestMethod]
    public async Task PrepareAsync_GroupCancelledMeanwhile_MakesNothing()
    {
        var data = new RunGroupTestData(previewApproved: false, background: true);
        data.AddSystemModel();
        data.ModelPhotosSucceed();
        var workflow = data.AddWorkflow(data.PersonConfig(), gateMode: "auto");
        var group = (await data.Service.StartAsync(workflow.Id, new(4, "cancelled"), default)).Value;
        data.GroupRows.Single().Status = "cancelled";

        await data.Service.PrepareAsync(group.Id, autoApprove: true, default);

        data.Lifestyle.Verify(x => x.GenerateForVideoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        data.Base.RunRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StartAsync_ManualMockupApproval_WaitsForTheSeller()
    {
        var data = new RunGroupTestData(previewApproved: false);

        var group = await data.StartAsync();

        group.Status.Should().Be("waiting_for_targets");
        data.PreviewMockups.Should().OnlyContain(x => x.ApprovalStatus == "pending");
    }

    [TestMethod]
    public async Task CancelAsync_ConfirmedGroup_CancelsEveryUnfinishedRun()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        await data.Service.ConfirmAsync(group.Id, new(1), default);

        var result = await data.Service.CancelAsync(group.Id, new(2), default);

        result.Value.Status.Should().Be("cancelled");
        data.Base.RunRows.Should().OnlyContain(run => run.Status == "cancelled");
    }

    [TestMethod]
    public async Task PlannerAdvanceNextAsync_QueuedRun_PlansItAsTheOwnerAndQueuesTheRender()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        await data.Service.ConfirmAsync(group.Id, new(1), default);
        Guid? actingAs = null;

        var advanced = await data.GroupPlanner.AdvanceNextAsync(owner => actingAs = owner, default);

        advanced.Should().BeTrue();
        actingAs.Should().Be(WorkflowTestData.Owner);
        var run = data.Base.RunRows.Single();
        run.Status.Should().Be("running");
        data.Base.JobRows.Should().ContainSingle(job => job.WorkflowRunId == run.Id && job.Kind == "render");
        (await data.GroupPlanner.AdvanceNextAsync(_ => { }, default)).Should().BeFalse();
    }

    [TestMethod]
    public async Task RerenderOfAChildRun_SettingsOfThePreviewProduct_AreRewrittenForItsDesign()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var other = data.AddProduct("Other");
        var (_, mockups) = data.AddDesign(other, 0, approved: true);
        await data.Service.ConfirmAsync(group.Id, new(1), default);
        var run = data.Base.RunRows.Single(x => x.ProductId == other.Id);
        run.Status = "failed";

        var result = await data.RunService.ActionAsync(run.Id, new(run.Revision, "retry", Config: data.SharedConfig()), default);

        result.IsSuccess.Should().BeTrue();
        var generate = WorkflowJson.Read<GenerateVideoConfig>(data.Base.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video").InputSnapshot);
        generate.AiScenes!.Select(x => x.MockupId).Should().Equal(mockups[0].Id, mockups[1].Id);
    }

    [TestMethod]
    public async Task StartAsync_ScenesWithAPerson_IdeogramMakesOneModelPhotoPerDesignFromItsHeroAndTheSystemModel()
    {
        var data = new RunGroupTestData();
        var model = data.AddSystemModel();
        data.ModelPhotosSucceed();
        var workflow = data.AddWorkflow(data.PersonConfig());

        (await data.Service.StartAsync(workflow.Id, new(4, "person"), default)).IsSuccess.Should().BeTrue();

        // Both scenes show a person; one image is made and both use it.
        data.PersonConfig().AiScenes.Should().HaveCountGreaterThan(1);
        data.Lifestyle.Verify(x => x.GenerateForVideoAsync(data.Preview.Id, data.PreviewMockups[0].Id, model.Id, It.IsAny<CancellationToken>()), Times.Once);
        data.Base.AssetRows.Should().ContainSingle(x => x.Role == ModelPhotoMockupService.Role).Which.ApprovalStatus.Should().Be("pending");
    }

    [TestMethod]
    public async Task StartAsync_ScenesWithAPerson_MakeTheModelPhotoWithoutASecondConfirmation()
    {
        var data = new RunGroupTestData();
        data.AddSystemModel();
        data.ModelPhotosSucceed();

        // The model photo is in the cost shown when the seller pressed Run: nothing else has to be sent for it.
        (await data.Service.StartAsync(data.AddWorkflow(data.PersonConfig()).Id, new(4, "shown-at-run"), default)).IsSuccess.Should().BeTrue();

        data.Lifestyle.Verify(x => x.GenerateForVideoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task StartAsync_ModelPhotoThatCannotBeMade_SaysWhyAndContinueTriesAgain()
    {
        var data = new RunGroupTestData();
        data.AddSystemModel();
        data.Lifestyle
            .Setup(x => x.GenerateForVideoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MockupResponse>(Error.Failure("Ideogram.Busy", "Ideogram is busy.")));
        var group = (await data.Service.StartAsync(data.AddWorkflow(data.PersonConfig()).Id, new(4, "busy"), default)).Value;

        group.Notice.Should().Contain("DAVIES T-Shirt").And.Contain("Ideogram is busy.");

        data.ModelPhotosSucceed();
        await data.Service.ConfirmAsync(group.Id, new(group.Revision), default);

        data.Base.AssetRows.Should().ContainSingle(x => x.Role == ModelPhotoMockupService.Role);
    }

    [TestMethod]
    public async Task StartAsync_ScenesWithoutAPerson_MakeNoModelPhoto()
    {
        var data = new RunGroupTestData();
        data.AddSystemModel();
        data.ModelPhotosSucceed();

        (await data.Service.StartAsync(data.AddWorkflow(data.AiConfig()).Id, new(4, "ai"), default)).IsSuccess.Should().BeTrue();

        data.Lifestyle.Verify(x => x.GenerateForVideoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StartAsync_DesignWithAModelPhotoAlready_GetsNoSecondOne()
    {
        var data = new RunGroupTestData();
        data.AddSystemModel();
        data.ModelPhotosSucceed();
        var workflow = data.AddWorkflow(data.PersonConfig());
        await data.Service.StartAsync(workflow.Id, new(4, "first"), default);

        await data.Service.StartAsync(workflow.Id, new(4, "second"), default);

        data.Base.AssetRows.Count(x => x.Role == ModelPhotoMockupService.Role).Should().Be(1);
    }

    [TestMethod]
    public async Task StartAsync_AutoApprovalWithScenesWithAPerson_StartsTheVideosWithTheirModelPhoto()
    {
        var data = new RunGroupTestData(previewApproved: false);
        data.AddSystemModel();
        data.ModelPhotosSucceed();
        var workflow = data.AddWorkflow(data.PersonConfig(), gateMode: "auto");

        var group = (await data.Service.StartAsync(workflow.Id, new(4, "person-auto"), default)).Value;

        group.Status.Should().Be("running");
        data.Base.AssetRows.Single(x => x.Role == ModelPhotoMockupService.Role).ApprovalStatus.Should().Be("approved");
        data.Base.RunRows.Should().ContainSingle();
    }

    [TestMethod]
    public async Task StartAsync_AutoApprovalWithAiScenes_StartsTheVideosWithoutASecondConfirmation()
    {
        var data = new RunGroupTestData(previewApproved: false);
        var workflow = data.AddWorkflow(data.AiConfig(), gateMode: "auto");

        var group = (await data.Service.StartAsync(workflow.Id, new(4, "ai-auto"), default)).Value;

        group.Status.Should().Be("running");
        group.Notice.Should().BeNull();
        data.Base.RunRows.Should().ContainSingle();
        data.Base.Quotes.Verify(x => x.QuoteBatchAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<PlannedVideo>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ResumeAsync_StoppedGroup_QueuesItsStoppedRunsAgainFromWhereTheyStopped()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        await data.Service.ConfirmAsync(group.Id, new(1), default);
        await data.GroupPlanner.AdvanceNextAsync(_ => { }, default);
        var stopped = (await data.Service.CancelAsync(group.Id, new(2), default)).Value;
        var run = data.Base.RunRows.Single();
        var video = data.Base.VideoRows.Single(x => x.WorkflowRunId == run.Id);
        video.Status.Should().Be("failed", "a video the stop left unfinished is not counted against the plan");

        var resumed = await data.Service.ResumeAsync(group.Id, new(stopped.Revision), default);

        resumed.IsSuccess.Should().BeTrue(resumed.IsFailure ? resumed.Error.Message : null);
        resumed.Value.Status.Should().Be("running");
        run.Status.Should().Be("queued");
        var steps = data.Base.NodeRows.Where(x => x.WorkflowRunId == run.Id).ToArray();
        steps.Should().NotContain(x => x.Status == "cancelled");
        steps.Single(x => x.NodeType == "approval-gate").Status.Should().Be("succeeded");

        (await data.GroupPlanner.AdvanceNextAsync(_ => { }, default)).Should().BeTrue();
        // Nothing changed, so the video is made again under the version the stop left unfinished.
        data.Base.VideoRows.Where(x => x.WorkflowRunId == run.Id).Should().ContainSingle().Which.Status.Should().Be("queued");
    }

    [TestMethod]
    public async Task ResumeAsync_GroupStoppedAtMockupApproval_WaitsThereAgain()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        var stopped = (await data.Service.CancelAsync(group.Id, new(group.Revision), default)).Value;

        var resumed = await data.Service.ResumeAsync(group.Id, new(stopped.Revision), default);

        resumed.Value.Status.Should().Be("waiting_for_targets");
    }

    [TestMethod]
    public async Task ResumeAsync_GroupThatIsNotStopped_IsRefused()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();

        var result = await data.Service.ResumeAsync(group.Id, new(group.Revision), default);

        result.Error.Code.Should().Be("InvalidCheckpoint");
    }

    [TestMethod]
    public async Task ResumeAsync_OneDesignsRun_WithALongerScene_RendersAgainAndBringsTheGroupBack()
    {
        var data = new RunGroupTestData();
        var group = await data.StartAsync();
        await data.Service.ConfirmAsync(group.Id, new(1), default);
        await data.GroupPlanner.AdvanceNextAsync(_ => { }, default);
        await data.Service.CancelAsync(group.Id, new(2), default);
        var run = data.Base.RunRows.Single();
        var generate = data.Base.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-video");
        var attempt = generate.Attempt;
        var longer = data.SharedConfig() with
        {
            DurationSeconds = 12,
            AiScenes = data.SharedConfig().AiScenes!.Select((scene, index) => index == 0 ? scene with { DurationSeconds = 7 } : scene).ToArray()
        };

        var result = await data.RunService.ActionAsync(run.Id, new(run.Revision, "resume", Config: longer), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        run.Status.Should().Be("running");
        generate.Attempt.Should().Be(attempt + 1);
        WorkflowJson.Read<GenerateVideoConfig>(generate.InputSnapshot).AiScenes![0].DurationSeconds.Should().Be(7);
        data.GroupRows.Single().Status.Should().Be("running");
    }

    [TestMethod]
    public async Task ConfirmAsync_ProductGivenItsOwnSettingsBeforeTheRun_RendersEachOfItsDesignsWithThem()
    {
        var data = new RunGroupTestData();
        var other = data.AddProduct("Birth Month Blooms");
        var (_, mockups) = data.AddDesign(other, 1, approved: true);
        var own = data.SharedConfig() with { DurationSeconds = 5, AiScenes = [data.SharedConfig().AiScenes![1]] };
        var workflow = data.AddWorkflow(data.SharedConfig() with
        {
            ProductConfigs = new Dictionary<Guid, GenerateVideoConfig> { [other.Id] = own }
        });
        var group = (await data.Service.StartAsync(workflow.Id, new(4, "own"), default)).Value;

        var waiting = (await data.Service.GetAsync(group.Id, default)).Value;
        var target = waiting.Targets.Single(x => x.ProductId == other.Id);
        target.Config!.DurationSeconds.Should().Be(5);
        target.HasOwnConfig.Should().BeFalse();
        target.Scenes.Select(x => x.MockupId).Should().Equal(mockups[1].Id);
        (await data.Service.ConfirmAsync(group.Id, new(group.Revision), default)).IsSuccess.Should().BeTrue();

        GenerateVideoConfig Rendered(Guid productId) => WorkflowJson.Read<GenerateVideoConfig>(data.Base.NodeRows
            .Single(x => x.NodeType == "generate-video" && x.WorkflowRunId == data.Base.RunRows.Single(run => run.ProductId == productId).Id).InputSnapshot);
        Rendered(other.Id).AiScenes.Should().ContainSingle().Which.MockupId.Should().Be(mockups[1].Id);
        Rendered(other.Id).ProductConfigs.Should().BeNull();
        Rendered(data.Preview.Id).AiScenes!.Select(x => x.MockupId).Should().Equal(data.PreviewMockups[0].Id, data.PreviewMockups[1].Id);
    }

    [TestMethod]
    public async Task StartAsync_AiScenesWithNoImageChosen_StartAndEachDesignUsesItsMainMockup()
    {
        var data = new RunGroupTestData();
        var open = data.AiConfig() with { AiScenes = data.AiConfig().AiScenes!.Select(scene => scene with { MockupId = null }).ToArray() };
        var workflow = data.AddWorkflow(data.SharedConfig() with
        {
            ProductConfigs = new Dictionary<Guid, GenerateVideoConfig> { [data.Preview.Id] = open }
        });

        var started = await data.Service.StartAsync(workflow.Id, new(4, "open"), default);

        started.IsSuccess.Should().BeTrue();
        var hero = SceneSourceResolver.Hero(data.PreviewMockups)!.Id;
        started.Value.Targets.Single().Scenes.Select(x => x.MockupId).Should().Equal(hero, hero);
        new APCS.Application.Features.Workflows.Validators.GenerateVideoConfigValidator().Validate(open).IsValid.Should().BeFalse();
    }

    [TestMethod]
    public async Task StartAsync_ProductSettingsThatMakeOneProductsVideoOnly_AreRefused()
    {
        var data = new RunGroupTestData();
        var workflow = data.AddWorkflow(data.SharedConfig() with
        {
            ProductConfigs = new Dictionary<Guid, GenerateVideoConfig> { [data.Preview.Id] = data.SharedConfig() with { Template = "product_on_model_pack" } }
        });

        var result = await data.Service.StartAsync(workflow.Id, new(4, "pack"), default);

        result.Error.Message.Should().Contain("product's own video settings");
    }

    [TestMethod]
    public async Task StartAsync_PublishWithoutVideo_WaitsForTheDesignsAndConfirmQueuesOneRunEachToPublish()
    {
        var data = new RunGroupTestData();
        var workflow = data.AddWorkflow();
        workflow.Definition = WorkflowJson.Write(WorkflowTestData.PublishWithoutVideoDefinition(data.Preview));
        var other = data.AddProduct("Birth Month Blooms");
        data.AddDesign(other, 1, approved: true);

        var started = await data.Service.StartAsync(workflow.Id, new(4, "publish"), default);
        started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : null);
        started.Value.Status.Should().Be("waiting_for_targets");
        started.Value.Targets.Should().HaveCount(2).And.OnlyContain(x => x.Selected && x.Scenes.Count == 0 && x.Config == null);

        var confirmed = await data.Service.ConfirmAsync(started.Value.Id, new(1), default);

        confirmed.IsSuccess.Should().BeTrue(confirmed.IsFailure ? confirmed.Error.Message : null);
        data.Base.RunRows.Should().HaveCount(2).And.OnlyContain(run => run.Status == "queued" && run.RunGroupId == started.Value.Id);
        data.Base.NodeRows.Should().NotContain(x => x.NodeType == "generate-video");
        data.Entitlements.AllowancesRequested.Should().NotContain(x => x.Item1 == UsageAllowances.Videos);
        data.Base.Quotes.Verify(x => x.VerifyBatchAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<PlannedVideo>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        (await data.GroupPlanner.AdvanceNextAsync(_ => { }, default)).Should().BeTrue();
        var planned = data.Base.RunRows.Single(x => x.Status != "queued");
        data.Base.NodeRows.Single(x => x.WorkflowRunId == planned.Id && x.NodeType == "publish-printify").Status.Should().Be("running");
    }

    [TestMethod]
    public async Task StartAsync_ListingAfterMockupApprovalWithoutVideo_WritesEachDesignsListing()
    {
        var data = new RunGroupTestData();
        data.Base.ListingSucceeds();
        var workflow = data.AddWorkflow();
        var publish = WorkflowTestData.PublishWithoutVideoDefinition(data.Preview);
        var listing = new WorkflowNode("listing", "generate-listing", "Listing content", new(800, 0), JsonSerializer.SerializeToElement(new { }));
        WorkflowNode[] nodes = [.. publish.Nodes.Where(x => x.Type != "publish-printify"), listing];
        workflow.Definition = WorkflowJson.Write(new WorkflowDefinition(2, nodes,
            nodes.Skip(1).Select((node, i) => new WorkflowEdge("edge-" + i, nodes[i].Id, node.Id)).ToArray()));

        var started = await data.Service.StartAsync(workflow.Id, new(4, "listing"), default);
        started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Message : null);
        (await data.Service.ConfirmAsync(started.Value.Id, new(1), default)).IsSuccess.Should().BeTrue();
        (await data.GroupPlanner.AdvanceNextAsync(_ => { }, default)).Should().BeTrue();

        var run = data.Base.RunRows.Should().ContainSingle().Subject;
        run.Status.Should().Be("completed");
        data.Base.NodeRows.Single(x => x.WorkflowRunId == run.Id && x.NodeType == "generate-listing").Status.Should().Be("succeeded");
    }

    [TestMethod]
    public async Task UpdateConfigAsync_GroupWithoutVideo_IsRefused()
    {
        var data = new RunGroupTestData();
        var workflow = data.AddWorkflow();
        workflow.Definition = WorkflowJson.Write(WorkflowTestData.PublishWithoutVideoDefinition(data.Preview));
        var group = (await data.Service.StartAsync(workflow.Id, new(4, "no-video"), default)).Value;

        var result = await data.Service.UpdateConfigAsync(group.Id,
            new(group.Revision, data.Preview.Id, data.PreviewDesign.Id.ToString(), data.SharedConfig()), default);

        result.Error.Message.Should().Contain("makes no video");
    }

    // Push to Printify runs beside the video: a run it failed still shows its video as made.
    [TestMethod]
    [DataRow("failed", "succeeded", "succeeded", "completed")]
    [DataRow("running", "succeeded", "waiting_for_review", "waiting_for_review")]
    [DataRow("succeeded", "failed", "pending", "failed")]
    [DataRow("failed", "running", "pending", "running")]
    // Stopped while only Printify was going: the finished video stays made.
    [DataRow("cancelled", "succeeded", "succeeded", "completed")]
    // Stopped while the video was rendering: the video is stopped.
    [DataRow("cancelled", "cancelled", "cancelled", "cancelled")]
    public void VideoStatusOf_ReadsTheVideoApartFromPushToPrintify(string printify, string generate, string review, string expected)
    {
        WorkflowNodeRun Step(string type, string status) => new() { Id = Guid.NewGuid(), NodeType = type, Status = status };
        var run = new WorkflowRun { Status = printify == "cancelled" ? "cancelled"
            : printify == "failed" && generate == "succeeded" && review == "succeeded" ? "failed" : "running" };
        var steps = new[]
        {
            Step("product-input", "succeeded"), Step("apply-mockup", "succeeded"), Step("approval-gate", "succeeded"),
            Step("publish-printify", printify), Step("generate-video", generate), Step("review-video", review),
        };

        WorkflowRunGroupService.VideoStatusOf(run, steps).Should().Be(expected);
    }

    [TestMethod]
    public void VideoStatusOf_RunWithoutAVideo_HasNone()
    {
        var steps = new[] { new WorkflowNodeRun { NodeType = "approval-gate", Status = "succeeded" }, new WorkflowNodeRun { NodeType = "publish-printify", Status = "failed" } };

        WorkflowRunGroupService.VideoStatusOf(new WorkflowRun { Status = "failed" }, steps).Should().BeNull();
    }

    [TestMethod]
    public async Task ListAsync_NamesTheDesignJobOfEachGroup_SoReopeningTheJobFindsItsVideos()
    {
        var data = new RunGroupTestData();
        var job = new BatchJob { Id = Guid.NewGuid(), BatchId = data.BatchId, UserId = WorkflowTestData.Owner };
        data.BatchJobRows.Add(job);
        var workflow = data.AddWorkflow();
        var fromJob = (await data.Service.StartAsync(workflow.Id, new(4, "job", job.Id), default)).Value;
        var fromBatch = (await data.Service.StartAsync(workflow.Id, new(4, "batch"), default)).Value;

        var listed = (await data.Service.ListAsync(workflow.Id, default)).Value;

        listed.Single(x => x.Id == fromJob.Id).BatchJobId.Should().Be(job.Id);
        listed.Single(x => x.Id == fromBatch.Id).BatchJobId.Should().BeNull();
    }

    /// <summary>Keeps what is handed to the background, as the worker's queue does.</summary>
    internal sealed class FakeStartQueue : IRunGroupStartQueue
    {
        private readonly HashSet<Guid> pending = [];
        public List<RunGroupStartRequest> Requests { get; } = [];
        public void Enqueue(RunGroupStartRequest request) { if (pending.Add(request.GroupId)) Requests.Add(request); }
        public bool IsPending(Guid groupId) => pending.Contains(groupId);
        public void Done(Guid groupId) => pending.Remove(groupId);
        public ValueTask<RunGroupStartRequest> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RunGroupTestData
    {
        public WorkflowTestData Base { get; } = new();
        public List<WorkflowRunGroup> GroupRows { get; } = [];
        public List<BatchJob> BatchJobRows { get; } = [];
        public List<BatchJobProduct> JobProductRows { get; } = [];
        public List<DesignImage> DesignRows { get; } = [];
        public TestEntitlements Entitlements { get; } = TestEntitlements.AllowAll();
        public Mock<IRunGroupPlannerQueue> Planner { get; } = new();
        public Mock<IWorkflowRunNotifier> Notifier { get; } = new();
        public Mock<ILifestyleMockupService> Lifestyle { get; } = new();
        public List<MockupTemplate> TemplateRows { get; } = [];
        public WorkflowRunGroupService Service { get; }
        public RunGroupPlanner GroupPlanner { get; }
        public WorkflowRunService RunService { get; }
        public Guid BatchId { get; } = Guid.NewGuid();
        public Guid FrontTemplate { get; } = Guid.NewGuid();
        public Guid BackTemplate { get; } = Guid.NewGuid();
        public Product Preview { get; }
        public DesignImage PreviewDesign { get; }
        public IReadOnlyList<MockupImage> PreviewMockups { get; }

        /// <summary>The queue a started group is handed to when it is made ready in the background; null runs it in the request.</summary>
        public FakeStartQueue? StartQueue { get; }

        public RunGroupTestData(bool previewApproved = true, bool background = false)
        {
            var groups = WorkflowTestData.Repository(GroupRows, x => x.Id);
            Base.State.Setup(x => x.LockRunGroupAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => GroupRows.SingleOrDefault(x => x.Id == id));
            Base.State.Setup(x => x.ClaimQueuedGroupRunAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((CancellationToken _) => Base.RunRows.Where(x => x.Status == "queued" && x.RunGroupId != null)
                    .OrderBy(x => x.CreatedAt).FirstOrDefault());
            Base.Storage.Setup(x => x.SignRead(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>()))
                .Returns((string key, string _, string? _, bool _) => $"https://cdn.test/{key}");
            var user = new Mock<ICurrentUser>();
            user.SetupGet(x => x.UserId).Returns(WorkflowTestData.Owner);
            var resolver = new RunTargetConfigResolver(Base.Assets.Object, Base.Products.Object, groups.Object);
            Service = new(user.Object, Base.Workflows.Object, groups.Object, Base.Runs.Object, Base.Nodes.Object, Base.Products.Object,
                Base.Assets.Object, WorkflowTestData.Repository(BatchJobRows, x => x.Id).Object,
                WorkflowTestData.Repository(JobProductRows, x => x.Id).Object, WorkflowTestData.Repository(DesignRows, x => x.Id).Object,
                Base.Jobs.Object, Base.State.Object, Base.Unit.Object, new WorkflowGraphValidator(Base.Capabilities, new()), resolver,
                Entitlements, Base.Storage.Object, Base.Time, Planner.Object, Notifier.Object, quotes: Base.Quotes.Object,
                lifestyle: Lifestyle.Object, mockupTemplates: WorkflowTestData.Repository(TemplateRows, x => x.Id).Object,
                startQueue: StartQueue = background ? new FakeStartQueue() : null, videos: Base.Videos.Object);
            GroupPlanner = new(Base.State.Object, Base.Unit.Object, Base.Runtime, Base.Time);
            RunService = new(user.Object, Base.Workflows.Object, Base.Runs.Object, Base.Nodes.Object, Base.Products.Object, Base.Videos.Object,
                Base.Jobs.Object, Base.State.Object, Base.Unit.Object, new(Base.Capabilities, new()), Base.Runtime, new(), Base.Capabilities,
                Base.Quotes.Object, Base.Time, TestEntitlements.AllowAll(), new(Base.Shops.Object, Base.Printify.Object),
                targetResolver: resolver);
            Preview = AddProduct("DAVIES T-Shirt");
            (PreviewDesign, PreviewMockups) = AddDesign(Preview, 0, previewApproved);
        }

        public Product AddProduct(string name)
        {
            var product = Base.AddProduct();
            product.BatchId = BatchId; product.Name = name; product.ProductType = "tshirt";
            return product;
        }

        /// <summary>A design placed on the front and back templates, in white.</summary>
        public (DesignImage Design, IReadOnlyList<MockupImage> Mockups) AddDesign(Product product, int variation, bool approved)
        {
            var design = new DesignImage { Id = Guid.NewGuid(), ProductId = product.Id, VariationIndex = variation, ApprovalStatus = "approved" };
            DesignRows.Add(design);
            var mockups = new[] { FrontTemplate, BackTemplate }.Select((template, index) => new MockupImage
            {
                Id = Guid.NewGuid(), ProductId = product.Id, DesignImageId = design.Id, MockupTemplateId = template, VariantKey = "white",
                Role = "Hero", ArtworkGroupKey = design.Id.ToString(), MetadataRevision = 1, ApprovedRevision = approved ? 1 : null,
                ApprovalStatus = approved ? "approved" : "pending", ContentHash = new string('a', 64), MockupWidthPx = 1080, MockupHeightPx = 1920,
                Regions = WorkflowJson.Write(new MockupRegions(new(.5, .5), new(.25, .2, .5, .6))), StorageKey = $"mockups/{Guid.NewGuid():N}",
                StorageVersion = "1", SourceType = "generated", CreatedAt = WorkflowTestData.Now.UtcDateTime.AddSeconds(index)
            }).ToArray();
            Base.AssetRows.AddRange(mockups);
            return (design, mockups);
        }

        /// <summary>Two Standard scenes picked on the preview product: front, then back.</summary>
        public GenerateVideoConfig SharedConfig() => new(Mode: "standard", DurationSeconds: 10, AiScenes:
        [
            new(MockupId: PreviewMockups[0].Id, Mode: "standard", DurationSeconds: 5, StandardMotionPreset: "zoom"),
            new(MockupId: PreviewMockups[1].Id, Mode: "standard", DurationSeconds: 5, StandardMotionPreset: "pan")
        ]);

        /// <summary>The same two scenes rendered by AI, with a template that films the product, not a person.</summary>
        public GenerateVideoConfig AiConfig() => SharedConfig() with
        {
            Mode = "ai_shot",
            AiScenes = SharedConfig().AiScenes!.Select(scene => scene with { Mode = "ai", TemplateCode = "cinematic_product_showcase" }).ToArray()
        };

        public Workflow AddWorkflow(GenerateVideoConfig? config = null, string gateMode = "manual") =>
            Base.AddWorkflow(Preview, config ?? SharedConfig(), gateMode);

        public void QuotesCost(decimal cost)
        {
            Base.Quotes.Setup(x => x.QuoteBatchAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<PlannedVideo>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(new AiShotQuoteResponse("budget-token", cost, 2, WorkflowTestData.Now.AddMinutes(10), "MiniMax-H3", "note")));
        }

        /// <summary>AI scenes that all show a person (Product On Model).</summary>
        public GenerateVideoConfig PersonConfig() => AiConfig() with
        {
            AiScenes = AiConfig().AiScenes!.Select(scene => scene with { TemplateCode = "lifestyle_in_use" }).ToArray()
        };

        /// <summary>A model photo of the shared library for t-shirts, usable for compositing.</summary>
        public MockupTemplate AddSystemModel(string name = "Ava")
        {
            var model = new MockupTemplate
            {
                Id = Guid.NewGuid(), Name = name, ProductType = "tshirt", Kind = MockupRules.ModelKind, IsActive = true, AllowRecolor = true,
                BaseImageUrl = "https://cdn.test/model.png", PrintAreaConfig = "{}", PrintMapsSourceUrl = "https://cdn.test/model.png", PrintMapsVersion = 1
            };
            TemplateRows.Add(model);
            return model;
        }

        /// <summary>Ideogram's model photo is stored as a Lifestyle image of the design, waiting for review, as the real service does.</summary>
        public void ModelPhotosSucceed() => Lifestyle
            .Setup(x => x.GenerateForVideoAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid productId, Guid garmentId, Guid modelId, CancellationToken _) =>
            {
                var garment = Base.AssetRows.Single(x => x.Id == garmentId);
                var photo = new MockupImage
                {
                    Id = Guid.NewGuid(), ProductId = productId, DesignImageId = garment.DesignImageId,
                    Role = ModelPhotoMockupService.Role, ArtworkGroupKey = garment.ArtworkGroupKey, VariantKey = "#FFFFFF", MetadataRevision = 1,
                    ApprovalStatus = "pending", ContentHash = new string('b', 64), MockupWidthPx = 1080, MockupHeightPx = 1920,
                    Regions = WorkflowJson.Write(new MockupRegions()), StorageKey = $"mockups/{Guid.NewGuid():N}", StorageVersion = "1",
                    SourceType = "uploaded", CreatedAt = WorkflowTestData.Now.UtcDateTime
                };
                Base.AssetRows.Add(photo);
                return Result.Success(new MockupResponse(photo.Id, productId, photo.SourceType, photo.Role, photo.ArtworkGroupKey, photo.VariantKey,
                    1, "pending", null, 1080, 1920, new MockupRegions(), "https://cdn.test/photo.png", []));
            });

        public async Task<RunGroupResponse> StartAsync(string gateMode = "manual")
        {
            var workflow = AddWorkflow(gateMode: gateMode);
            var result = await Service.StartAsync(workflow.Id, new(4, Guid.NewGuid().ToString()), default);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
            return result.Value;
        }
    }
}
