using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Common;
using APCS.Application.Features.Workflows;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowRunServiceTests
{
    [TestMethod]
    public async Task ActionAsync_InvalidRerenderConfiguration_DoesNotCreateVersion()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "rerender", Config: new(DurationSeconds: 31)), default);
        result.IsFailure.Should().BeTrue();
        fixture.VideoRows.Should().HaveCount(1);
        fixture.JobRows.Should().BeEmpty();
        fixture.NodeRows.Single(x => x.NodeType == "generate-video").Attempt.Should().Be(1);
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StartAsync_ForeignWorkflow_DoesNotCreateRunEvenWhenProductIsOwned()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct()); workflow.UserId = Guid.NewGuid();
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "foreign-workflow"), default);
        result.Error.Code.Should().Be("WorkflowRunNotFound");
        fixture.RunRows.Should().BeEmpty();
        fixture.VideoRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAsync_ForeignRun_DoesNotRevealCheckpointData()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct()); run.UserId = Guid.NewGuid();
        var result = await fixture.RunService.GetAsync(run.Id, default);
        result.Error.Code.Should().Be("WorkflowRunNotFound");
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ActionAsync_LegacyAiBackgroundRerender_DoesNotBypassModeCapability()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "rerender", Config: new(Mode: "ai_background")), default);
        result.Error.Code.Should().Be("UnsupportedVideoMode");
        fixture.VideoRows.Should().HaveCount(1);
        fixture.JobRows.Should().BeEmpty();
        run.Status.Should().Be("waiting_for_review");
    }

    [TestMethod]
    public async Task StartAsync_ValidWorkflow_SnapshotsDefinitionAndPausesAtMockupApproval()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        using var cancellation = new CancellationTokenSource();
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "first-run"), cancellation.Token);
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("waiting_for_input");
        var run = fixture.RunRows.Single();
        run.DefinitionSnapshot.Should().Be(workflow.Definition);
        run.WorkflowRevision.Should().Be(4);
        fixture.NodeRows.Should().HaveCount(6);
        fixture.NodeRows.Single(x => x.NodeType == "product-input").Status.Should().Be("succeeded");
        fixture.NodeRows.Single(x => x.NodeType == "approval-gate").Status.Should().Be("waiting_for_input");
        fixture.VideoRows.Should().BeEmpty();
        fixture.JobRows.Should().BeEmpty();
        fixture.State.Verify(x => x.LockOwnerAsync(WorkflowTestData.Owner, cancellation.Token), Times.Once);
        fixture.Transaction.Verify(x => x.CommitAsync(cancellation.Token), Times.Once);
    }

    [TestMethod]
    public async Task StartAsync_AutoMockupApproval_ApprovesTheWaitingMockupsAndGoesOn()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var waiting = WorkflowTestData.Asset(product.Id);
        waiting.ApprovalStatus = "pending";
        waiting.ApprovedRevision = null;
        fixture.AssetRows.Add(waiting);
        var workflow = fixture.AddWorkflow(product, gateMode: "auto");

        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "auto-run"), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        fixture.NodeRows.Single(x => x.NodeType == "approval-gate").Status.Should().Be("succeeded");
        waiting.ApprovalStatus.Should().Be("approved");
        waiting.ApprovedRevision.Should().Be(waiting.MetadataRevision);
        waiting.ApprovedBy.Should().Be(WorkflowTestData.Owner);
    }

    [TestMethod]
    public async Task StartAsync_AutoMockupApprovalWithAnAiScene_StillWaitsForTheCostToBeConfirmed()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var waiting = WorkflowTestData.Asset(product.Id);
        waiting.ApprovalStatus = "pending";
        fixture.AssetRows.Add(waiting);
        var ai = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 6, AiScenes: [new AiVideoSceneOptions(MockupId: waiting.Id, DurationSeconds: 6)]);
        var workflow = fixture.AddWorkflow(product, ai, gateMode: "auto");

        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "auto-ai-run"), default);

        result.Value.Status.Should().Be("waiting_for_input");
        fixture.NodeRows.Single(x => x.NodeType == "approval-gate").Status.Should().Be("waiting_for_input");
        waiting.ApprovalStatus.Should().Be("approved");
    }

    [TestMethod]
    public async Task StartAsync_AutoMockupApprovalWithoutAnyMockup_WaitsForOne()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct(), gateMode: "auto");

        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "auto-empty-run"), default);

        result.Value.Status.Should().Be("waiting_for_input");
        fixture.NodeRows.Single(x => x.NodeType == "approval-gate").Status.Should().Be("waiting_for_input");
    }

    [TestMethod]
    public async Task StartAsync_PrintifyNotConnected_RefusesBeforeCreatingRun()
    {
        var fixture = new WorkflowTestData();
        fixture.PrintifyIs(PrintifyStatuses.NotConnected);
        var workflow = fixture.AddPublishWorkflow(fixture.AddProduct());
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "no-printify"), default);
        result.Error.Code.Should().Be("shop.printify_not_connected");
        fixture.RunRows.Should().BeEmpty();
        fixture.NodeRows.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(PrintifyStatuses.KeyInvalid, "shop.printify_key_invalid")]
    [DataRow(PrintifyStatuses.NoStore, "shop.printify_no_store")]
    public async Task StartAsync_PrintifyNotUsable_NamesWhatToFix(string status, string code)
    {
        var fixture = new WorkflowTestData();
        fixture.PrintifyIs(status);
        var workflow = fixture.AddPublishWorkflow(fixture.AddProduct());
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "printify-" + status), default);
        result.Error.Code.Should().Be(code);
        fixture.RunRows.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(ShopConnectionStatuses.NotConnected, "shop.not_connected")]
    [DataRow(ShopConnectionStatuses.ReconnectRequired, "shop.reconnect_required")]
    public async Task StartAsync_EtsyNotConnected_RefusesBeforeCreatingRun(string status, string code)
    {
        var fixture = new WorkflowTestData();
        fixture.EtsyIs(status);
        var workflow = fixture.AddPublishWorkflow(fixture.AddProduct());
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "etsy-" + status), default);
        result.Error.Code.Should().Be(code);
        result.Error.Message.Should().Contain("Shop page");
        fixture.RunRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StartAsync_ShopsConnectedOrOnlyUnreachable_StartsRun()
    {
        var fixture = new WorkflowTestData();
        fixture.PrintifyIs(PrintifyStatuses.Unavailable);
        var workflow = fixture.AddPublishWorkflow(fixture.AddProduct());
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "publish-run"), default);
        result.IsSuccess.Should().BeTrue();
        fixture.RunRows.Should().ContainSingle();
        fixture.NodeRows.Should().Contain(x => x.NodeType == "publish-etsy" && x.Status == "pending");
    }

    [TestMethod]
    public async Task StartAsync_WithoutPublishSteps_DoesNotAskShops()
    {
        var fixture = new WorkflowTestData();
        fixture.EtsyIs(ShopConnectionStatuses.NotConnected);
        fixture.PrintifyIs(PrintifyStatuses.NotConnected);
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "no-publish"), default);
        result.IsSuccess.Should().BeTrue();
        fixture.Shops.Verify(x => x.GetMyShopAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Printify.Verify(x => x.GetConnectionAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StartAsync_SameIdempotencyKey_ReturnsExistingRunWithoutDuplicatingVideoOrNodes()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        var request = new StartRunRequest(4, "stable-request");
        var first = await fixture.RunService.StartAsync(workflow.Id, request, default);
        var repeated = await fixture.RunService.StartAsync(workflow.Id, request, default);
        repeated.Value.Id.Should().Be(first.Value.Id);
        fixture.RunRows.Should().HaveCount(1);
        fixture.NodeRows.Should().HaveCount(6);
        fixture.VideoRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StartAsync_ReusedKeyForDifferentRevision_ReturnsConflict()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        await fixture.RunService.StartAsync(workflow.Id, new(4, "same-key"), default);
        var result = await fixture.RunService.StartAsync(workflow.Id, new(5, "same-key"), default);
        result.Error.Code.Should().Be("IdempotencyConflict");
        fixture.RunRows.Should().HaveCount(1);
    }

    [TestMethod]
    [DataRow("foreign_owner")]
    [DataRow("wrong_batch")]
    [DataRow("deleted")]
    public async Task StartAsync_UnavailableProduct_DoesNotCreateRun(string condition)
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var workflow = fixture.AddWorkflow(product);
        if (condition == "foreign_owner") product.UserId = Guid.NewGuid();
        if (condition == "wrong_batch") product.BatchId = Guid.NewGuid();
        if (condition == "deleted") product.DeletedAt = WorkflowTestData.Now.UtcDateTime;
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "unauthorized"), default);
        result.Error.Code.Should().Be("ProductNotFound");
        fixture.RunRows.Should().BeEmpty();
        fixture.NodeRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StartAsync_LegacyAiBackgroundRequest_RejectsBeforeCreatingRun()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct(), new(Mode: "ai_background"));
        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "ai-request"), default);
        result.Error.Code.Should().Be("UnsupportedVideoMode");
        fixture.RunRows.Should().BeEmpty();
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StartAsync_AiShotWorkflow_PausesAtApprovalWithoutChargingOrQueuingJob()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct(), new(Mode: "ai_shot", AiShotOptions: new()));

        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "ai-request"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("waiting_for_input");
        fixture.JobRows.Should().BeEmpty();
        fixture.Quotes.Verify(x => x.VerifyAsync(It.IsAny<WorkflowRun>(), It.IsAny<GenerateVideoConfig>(),
            It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StartAsync_PublishWithoutVideo_WaitsAtMockupApproval()
    {
        var fixture = new WorkflowTestData();
        fixture.AddAsset(fixture.AddProduct());
        var workflow = fixture.AddPublishWithoutVideoWorkflow(fixture.ProductRows.Single());

        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "publish-only"), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Status.Should().Be("waiting_for_input");
        fixture.NodeRows.Single(x => x.NodeType == "approval-gate").Status.Should().Be("waiting_for_input");
        fixture.NodeRows.Single(x => x.NodeType == "publish-printify").Status.Should().Be("pending");
        fixture.VideoRows.Should().BeEmpty();
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActionAsync_ContinueAtMockupApprovalWithoutVideo_QueuesThePublishStep()
    {
        var fixture = new WorkflowTestData();
        var asset = fixture.AddAsset(fixture.AddProduct());
        var workflow = fixture.AddPublishWithoutVideoWorkflow(fixture.ProductRows.Single());
        var started = (await fixture.RunService.StartAsync(workflow.Id, new(4, "publish-only"), default)).Value;

        var result = await fixture.RunService.ActionAsync(started.Id, new(started.Revision, "resume_mockups"), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Status.Should().Be("running");
        var gate = fixture.NodeRows.Single(x => x.NodeType == "approval-gate");
        gate.Status.Should().Be("succeeded");
        gate.OutputSnapshot.Should().Contain(asset.Id.ToString());
        var publish = fixture.NodeRows.Single(x => x.NodeType == "publish-printify");
        publish.Status.Should().Be("running");
        publish.Stage.Should().Be("Queued");
        fixture.VideoRows.Should().BeEmpty();
        // Nothing paid for follows Mockup Approval without a video, so no cost is confirmed.
        fixture.Quotes.Verify(x => x.VerifyAsync(It.IsAny<WorkflowRun>(), It.IsAny<GenerateVideoConfig>(),
            It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StartAsync_PublishWithoutVideoAndAutoApproval_ApprovesAndGoesStraightToPublishing()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var waiting = WorkflowTestData.Asset(product.Id);
        waiting.ApprovalStatus = "pending";
        waiting.ApprovedRevision = null;
        fixture.AssetRows.Add(waiting);
        var workflow = fixture.AddPublishWithoutVideoWorkflow(product, gateMode: "auto");

        var result = await fixture.RunService.StartAsync(workflow.Id, new(4, "publish-auto"), default);

        result.Value.Status.Should().Be("running");
        waiting.ApprovalStatus.Should().Be("approved");
        fixture.NodeRows.Single(x => x.NodeType == "approval-gate").Status.Should().Be("succeeded");
        fixture.NodeRows.Single(x => x.NodeType == "publish-printify").Status.Should().Be("running");
    }

    [TestMethod]
    [DataRow("retry")]
    [DataRow("rerender")]
    [DataRow("approve_video")]
    public async Task ActionAsync_VideoActionOrRetryWithoutAFailedPublish_IsRefusedOnARunWithoutVideo(string action)
    {
        var fixture = new WorkflowTestData();
        fixture.AddAsset(fixture.AddProduct());
        var workflow = fixture.AddPublishWithoutVideoWorkflow(fixture.ProductRows.Single());
        var started = (await fixture.RunService.StartAsync(workflow.Id, new(4, "publish-only"), default)).Value;

        var result = await fixture.RunService.ActionAsync(started.Id, new(started.Revision, action), default);

        result.Error.Code.Should().Be("InvalidCheckpoint");
    }

    [TestMethod]
    public async Task ActionAsync_RetryAFailedPublishWithoutVideo_RunsThePublishStepAgain()
    {
        var fixture = new WorkflowTestData();
        fixture.AddAsset(fixture.AddProduct());
        var workflow = fixture.AddPublishWithoutVideoWorkflow(fixture.ProductRows.Single(), gateMode: "auto");
        var started = (await fixture.RunService.StartAsync(workflow.Id, new(4, "publish-auto"), default)).Value;
        var run = fixture.RunRows.Single();
        var publish = fixture.NodeRows.Single(x => x.NodeType == "publish-printify");
        publish.Status = "failed"; publish.ErrorMessage = "Printify is down.";
        run.Status = "failed";

        var result = await fixture.RunService.ActionAsync(started.Id, new(run.Revision, "retry"), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        publish.Status.Should().Be("running");
        publish.Attempt.Should().Be(2);
        run.Status.Should().Be("running");
    }

    [TestMethod]
    public async Task ActionAsync_NoApprovedMockups_CannotResumeOrRender()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct(), "waiting_for_input");
        fixture.NodeRows.Single(x => x.NodeType == "approval-gate").Status = "waiting_for_input";
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "resume_mockups"), default);
        result.Error.Code.Should().Be("WaitingForInput");
        run.Status.Should().Be("waiting_for_input");
        fixture.VideoRows.Should().BeEmpty();
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActionAsync_ApprovedMockups_QueuesRenderAndKeepsReviewPending()
    {
        var fixture = new WorkflowTestData();
        var product = fixture.AddProduct();
        var asset = fixture.AddAsset(product);
        var run = fixture.AddRun(product, "waiting_for_input");
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "resume_mockups"), default);
        result.Value.Status.Should().Be("running");
        result.Value.Revision.Should().Be(8);
        var video = fixture.VideoRows.Single();
        video.Mode.Should().Be("standard");
        video.ApprovalStatus.Should().Be("pending");
        video.VersionNumber.Should().Be(1);
        fixture.SceneRows.Should().HaveCount(2).And.OnlyContain(x => x.MockupImageId == asset.Id && x.SourceRevision == 3);
        fixture.SceneRows.Select(x => x.SceneOrder).Should().Equal(1, 2);
        fixture.SceneRows.Select(x => WorkflowJson.Read<ScenePlan>(x.SceneConfig).SceneOrder).Should().Equal(0, 1);
        fixture.JobRows.Single().Kind.Should().Be("render");
        fixture.JobRows.Single().Status.Should().Be("queued");
        fixture.NodeRows.Single(x => x.NodeType == "review-video").Status.Should().Be("pending");
        fixture.ExportRows.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("old_video")]
    [DataRow("old_review_revision")]
    [DataRow("old_run_revision")]
    public async Task ActionAsync_StaleExactApproval_DoesNotApproveCandidateOrExport(string condition)
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var candidate = fixture.VideoRows.Single();
        var id = condition == "old_video" ? Guid.NewGuid() : candidate.Id;
        var result = await fixture.RunService.ActionAsync(run.Id, new(condition == "old_run_revision" ? 6 : 7,
            "approve_video", id, condition == "old_review_revision" ? 1 : 2), default);
        result.Error.Code.Should().Be("StaleRevision");
        candidate.ApprovalStatus.Should().Be("pending");
        fixture.ExportRows.Should().BeEmpty();
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActionAsync_ApproveVideo_PacksTheProductsApprovedModelPhotoImages()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var video = fixture.VideoRows.Single();
        var approved = AddModelPhotoImage(fixture, run.ProductId, "approved");
        var pending = AddModelPhotoImage(fixture, run.ProductId, "pending");

        await fixture.RunService.ActionAsync(run.Id, new(7, "approve_video", video.Id, 2), default);

        var manifest = System.Text.Json.Nodes.JsonNode.Parse(fixture.ExportRows.Single().Manifest)!;
        var photo = manifest["modelPhotos"]!.AsArray().Should().ContainSingle().Subject!;
        ((string)photo["mockupId"]!).Should().Be(approved.Id.ToString());
        ((string)photo["source"]!).Should().Be($"model-photos/{approved.Id}.png");
        ((string)photo["sourceHash"]!).Should().Be(approved.ContentHash);
        fixture.ExportRows.Single().Manifest.Should().NotContain(pending.Id.ToString());
    }

    [TestMethod]
    public async Task ActionAsync_ApproveVideoWithoutModelPhotoImages_KeepsTheManifestAsBefore()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);

        await fixture.RunService.ActionAsync(run.Id, new(7, "approve_video", fixture.VideoRows.Single().Id, 2), default);

        fixture.ExportRows.Single().Manifest.Should().NotContain("modelPhotos");
    }

    private static MockupImage AddModelPhotoImage(WorkflowTestData fixture, Guid productId, string approval)
    {
        var image = new MockupImage
        {
            Id = Guid.NewGuid(), ProductId = productId, SourceType = "generated", Role = "Lifestyle", StorageProvider = "cloudinary",
            StorageKey = "mockups/model", StorageVersion = "1", ContentHash = new string('e', 64), MockupWidthPx = 1000, MockupHeightPx = 1500,
            ApprovalStatus = approval, MetadataRevision = 1, Regions = "{}", CreatedAt = WorkflowTestData.Now.UtcDateTime
        };
        fixture.AssetRows.Add(image);
        return image;
    }

    [TestMethod]
    public async Task ActionAsync_ApproveExactRevision_CreatesExportFromOnlyApprovedVideo()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var video = fixture.VideoRows.Single();
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "approve_video", video.Id, 2), default);
        result.Value.Status.Should().Be("running");
        video.ApprovalStatus.Should().Be("approved");
        video.ApprovedBy.Should().Be(WorkflowTestData.Owner);
        video.ApprovedAt.Should().Be(WorkflowTestData.Now.UtcDateTime);
        video.ReviewRevision.Should().Be(3);
        var export = fixture.ExportRows.Single();
        export.Status.Should().Be("preparing");
        export.Manifest.Should().Contain(video.Id.ToString()).And.Contain("standard").And.NotContain("https:").And.NotContain("prompt");
        fixture.ItemRows.Single().PromoVideoId.Should().Be(video.Id);
        fixture.JobRows.Single().Kind.Should().Be("export_zip");
    }

    [TestMethod]
    public async Task ActionAsync_ApproveVideo_ExportsPhotosInEtsyOrder_WithAGuideToListByHand()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var video = fixture.VideoRows.Single();
        var real = fixture.AssetRows.Single();
        real.CreatedAt = WorkflowTestData.Now.UtcDateTime;
        // Made earlier, but by AI: it goes after the photo of the real garment.
        var ai = WorkflowTestData.Asset(real.ProductId, "Lifestyle");
        ai.SourceType = "ai_generated";
        ai.CreatedAt = WorkflowTestData.Now.UtcDateTime.AddHours(-1);
        fixture.AssetRows.Add(ai);

        await fixture.RunService.ActionAsync(run.Id, new(7, "approve_video", video.Id, 2), default);

        var payload = WorkflowJson.Read<ExportJobPayload>(fixture.JobRows.Single().Payload);
        payload.ApprovedMockups!.Select(x => (x.Id, x.AiMade)).Should().Equal((real.Id, false), (ai.Id, true));
        payload.EtsyGuide.Should().Contain("mockup/mockup-01.png").And.Contain("mockup/mockup-02.png  [made with AI]")
            .And.Contain($"video/final/v001__{video.Id}/video.mp4").And.Contain(EtsyListingText.AiDisclosure);
        fixture.ItemRows.Single().IncludeMockupImages.Should().BeTrue();
    }

    [TestMethod]
    public void EtsyGuide_AWorkflowThatPushesToPrintify_WarnsThatAHandMadeListingIsNotFulfilled()
    {
        var guide = ExportEtsyGuide.Build("Cat tee", "tshirt", [new("mockup/mockup-01.png", false)], [], "video/final/v001__x/video.mp4",
            hasListing: true, etsy: null, viaPrintify: true);

        guide.Should().Contain("not linked to Printify").And.Contain("keep its last paragraph").And.Contain("Production partners");
    }

    [TestMethod]
    public async Task ActionAsync_RejectVideo_KeepsReviewCheckpointWithoutExport()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var video = fixture.VideoRows.Single();
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "reject_video", video.Id, 2), default);
        result.Value.Status.Should().Be("waiting_for_review");
        video.ApprovalStatus.Should().Be("rejected");
        video.ApprovedBy.Should().BeNull();
        video.ReviewRevision.Should().Be(3);
        fixture.ExportRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActionAsync_Rerender_CreatesNewVersionAndPreservesOldArtifact()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var old = fixture.VideoRows.Single();
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "rerender", Config: new(TextOverlay: "Version two")), default);
        result.Value.Status.Should().Be("running");
        fixture.VideoRows.Should().HaveCount(2);
        var candidate = fixture.VideoRows.Single(x => x.Id != old.Id);
        candidate.SeriesId.Should().Be(old.SeriesId);
        candidate.VersionNumber.Should().Be(2);
        candidate.ApprovalStatus.Should().Be("pending");
        candidate.ConfigSnapshot.Should().Contain("Version two");
        old.StorageKey.Should().Be("private/approved-video");
        old.Status.Should().Be("completed");
        fixture.NodeRows.Single(x => x.NodeType == "generate-video").Attempt.Should().Be(2);
    }

    [TestMethod]
    public async Task ActionAsync_RerenderWithStaleStoryboardFingerprint_ReturnsConflictWithoutChangingCandidate()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video");
        var originalInput = generate.InputSnapshot;

        var result = await fixture.RunService.ActionAsync(run.Id,
            new(7, "rerender", Config: new(TemplateVersion: 2), ExpectedStoryboardFingerprint: "stale"), default);

        result.Error.Code.Should().Be("StoryboardChanged");
        fixture.VideoRows.Should().ContainSingle();
        fixture.JobRows.Should().BeEmpty();
        generate.Attempt.Should().Be(1);
        generate.InputSnapshot.Should().Be(originalInput);
    }

    [TestMethod]
    public async Task ActionAsync_RerenderWithMatchingStoryboardFingerprint_QueuesExactPreview()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture);
        var config = new GenerateVideoConfig(TemplateVersion: 2);
        var fingerprint = new VideoStoryboardPlanner().Plan(fixture.ProductRows.Single(), fixture.AssetRows, config).Value.Fingerprint;

        var result = await fixture.RunService.ActionAsync(run.Id,
            new(7, "rerender", Config: config, ExpectedStoryboardFingerprint: fingerprint), default);

        result.Value.Status.Should().Be("running");
        fixture.VideoRows.Should().HaveCount(2);
        fixture.VideoRows.MaxBy(x => x.VersionNumber)!.Fingerprint.Should().Be(fingerprint);
    }

    [TestMethod]
    public async Task ActionAsync_RetryWithTheSameSettingsFromTheCanvas_CarriesOnWithTheFailedVersion()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture); run.Status = "failed";
        var existing = fixture.VideoRows.Single(); existing.Status = "failed";
        existing.Fingerprint = new VideoStoryboardPlanner().Plan(fixture.ProductRows.Single(), fixture.AssetRows, new()).Value.Fingerprint;
        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video"); generate.Status = "failed";

        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "retry", Config: new()), default);

        result.Value.Status.Should().Be("running");
        fixture.VideoRows.Should().ContainSingle();
        generate.Attempt.Should().Be(1);
    }

    [TestMethod]
    public async Task ActionAsync_RetryWithChangedSettingsFromTheCanvas_RendersThemAsANewVersion()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture); run.Status = "failed";
        fixture.VideoRows.Single().Status = "failed";
        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video"); generate.Status = "failed";

        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "retry", Config: new(TextOverlay: "Second try")), default);

        result.Value.Status.Should().Be("running");
        generate.Attempt.Should().Be(2);
        generate.InputSnapshot.Should().Contain("Second try");
        fixture.VideoRows.Should().HaveCount(2);
        fixture.VideoRows.MaxBy(x => x.VersionNumber)!.ConfigSnapshot.Should().Contain("Second try");
    }

    [TestMethod]
    public async Task ActionAsync_RetryListing_WritesTheListingWhileTheRunKeepsWaiting()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct(), "waiting_for_input");
        var listing = fixture.AddListingStep(run, "failed");
        fixture.ListingSucceeds();

        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "retry_listing"), default);

        result.Value.Status.Should().Be("waiting_for_input");
        result.Value.Revision.Should().Be(8);
        listing.Status.Should().Be("succeeded");
        fixture.JobRows.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("running", "failed")]
    [DataRow("cancelled", "failed")]
    [DataRow("waiting_for_input", "succeeded")]
    public async Task ActionAsync_RetryListingWhenItCannotRun_IsRefusedWithoutCallingGemini(string runStatus, string listingStatus)
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct(), runStatus);
        fixture.AddListingStep(run, listingStatus);

        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "retry_listing"), default);

        result.Error.Code.Should().Be("InvalidCheckpoint");
        fixture.ListingContent.VerifyNoOtherCalls();
        run.Revision.Should().Be(7);
    }

    [TestMethod]
    [DataRow("retry")]
    [DataRow("rerender")]
    [DataRow("resume_mockups")]
    [DataRow("approve_video")]
    public async Task ActionAsync_VideoActionOnAListingOnlyRun_IsRefusedInsteadOfFailing(string action)
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddListingOnlyRun(fixture.AddProduct(), "failed");

        var result = await fixture.RunService.ActionAsync(run.Id, new(7, action), default);

        result.Error.Code.Should().Be("InvalidCheckpoint");
    }

    [TestMethod]
    public async Task ActionAsync_RetryFailedSameFingerprint_ReusesLogicalVideoAndScenes()
    {
        var fixture = new WorkflowTestData();
        var run = PrepareReview(fixture); run.Status = "failed";
        var existing = fixture.VideoRows.Single(); existing.Status = "failed";
        existing.Fingerprint = new VideoStoryboardPlanner().Plan(fixture.ProductRows.Single(), fixture.AssetRows, new()).Value.Fingerprint;
        var generate = fixture.NodeRows.Single(x => x.NodeType == "generate-video"); generate.Status = "failed";
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "retry"), default);
        result.Value.Status.Should().Be("running");
        fixture.VideoRows.Should().HaveCount(1);
        fixture.SceneRows.Should().HaveCount(1);
        existing.Status.Should().Be("queued");
        existing.VersionNumber.Should().Be(1);
        generate.Attempt.Should().Be(1);
        WorkflowJson.Read<RenderJobPayload>(fixture.JobRows.Single().Payload).VideoId.Should().Be(existing.Id);
    }

    [TestMethod]
    public async Task GetAsync_RunningRunWithFailedJob_OnlyReads()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var job = fixture.AddJob(run); job.Status = "failed"; job.ErrorMessage = "Worker lease expired after maximum attempts.";
        var result = await fixture.RunService.GetAsync(run.Id, default);
        result.Value.Status.Should().Be("running");
        run.Status.Should().Be("running");
        fixture.Unit.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.State.Verify(x => x.LockRunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetAsync_StoredRun_ReadsSnapshotAndCheckpointState()
    {
        var fixture = new WorkflowTestData();
        var workflow = fixture.AddWorkflow(fixture.AddProduct());
        var started = await fixture.RunService.StartAsync(workflow.Id, new(4, "durable"), default);
        var result = await fixture.RunService.GetAsync(started.Value.Id, default);
        result.Value.Id.Should().Be(started.Value.Id);
        result.Value.Status.Should().Be("waiting_for_input");
        result.Value.Nodes.Single(x => x.NodeType == "approval-gate").Status.Should().Be("waiting_for_input");
        fixture.RunRows.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task ActionAsync_Cancel_RevokesLeaseAndPreservesCompletedCheckpoints()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct());
        var input = fixture.NodeRows.Single(x => x.NodeType == "product-input"); input.Status = "succeeded";
        var job = fixture.AddJob(run);
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "cancel"), default);
        result.Value.Status.Should().Be("cancelled");
        run.CompletedAt.Should().Be(WorkflowTestData.Now.UtcDateTime);
        job.Status.Should().Be("cancelled");
        job.LeaseToken.Should().BeNull();
        input.Status.Should().Be("succeeded");
        fixture.NodeRows.Where(x => x != input).Should().OnlyContain(x => x.Status == "cancelled");
        fixture.ExportRows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ActionAsync_ForeignRun_DoesNotCancelOrRevealState()
    {
        var fixture = new WorkflowTestData();
        var run = fixture.AddRun(fixture.AddProduct()); run.UserId = Guid.NewGuid();
        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "cancel"), default);
        result.Error.Code.Should().Be("WorkflowRunNotFound");
        run.Status.Should().Be("running");
        fixture.Unit.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ActionAsync_RegenerateStoredSceneInCancelledRun_ReusesOtherSceneAndReopensReview()
    {
        var fixture = new WorkflowTestData();
        var (run, config, job) = await PrepareCancelledAiVideoAsync(fixture);
        var oldVideo = fixture.VideoRows.Single();

        var result = await fixture.RunService.ActionAsync(run.Id,
            new(7, "regenerate_ai_shot", Config: config, AiShotQuoteToken: "confirmed", SceneOrder: 0), default);

        result.IsSuccess.Should().BeTrue();
        run.Status.Should().Be("running");
        run.CompletedAt.Should().BeNull();
        fixture.VideoRows.Should().HaveCount(2);
        oldVideo.Status.Should().Be("completed");
        oldVideo.StorageKey.Should().Be("private/approved-video");
        job.Status.Should().Be("queued");
        var payload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload);
        payload.AiShotStates.Single(item => item.SceneOrder == 0).Status.Should().Be("pending");
        var retained = payload.AiShotStates.Single(item => item.SceneOrder == 1);
        retained.Status.Should().Be("stored");
        retained.StorageKey.Should().Be("private/scene-1.mp4");
        retained.StorageVersion.Should().Be("1");
        fixture.NodeRows.Single(item => item.NodeType == "review-video").Status.Should().Be("pending");
        fixture.Quotes.Verify(item => item.VerifyAsync(run, config, "confirmed", true, 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow("missing_target", "SceneNotStored")]
    [DataRow("other_incomplete", "OtherScenesIncomplete")]
    [DataRow("other_mismatch", "OtherScenesIncomplete")]
    [DataRow("unfinished_video", "InvalidCheckpoint")]
    [DataRow("all_scenes", "InvalidCheckpoint")]
    [DataRow("completed_run", "InvalidCheckpoint")]
    public async Task ActionAsync_CannotSafelyReopenCancelledVideo_LeavesArtifactsUntouched(string condition, string expectedCode)
    {
        var fixture = new WorkflowTestData();
        var (run, config, job) = await PrepareCancelledAiVideoAsync(fixture);
        var payload = WorkflowJson.Read<AiRenderJobPayload>(job.Payload);
        if (condition is "missing_target" or "other_incomplete")
            job.Payload = WorkflowJson.Write(payload with { AiShotStates = payload.AiShotStates.Select(item =>
                item.SceneOrder == (condition == "missing_target" ? 0 : 1)
                    ? item with { Status = "pending", StorageKey = null, StorageVersion = null } : item).ToArray() });
        if (condition == "other_mismatch")
            job.Payload = WorkflowJson.Write(payload with { AiShotStates = payload.AiShotStates.Select(item =>
                item.SceneOrder == 1 ? item with { Fingerprint = "unrelated-clip" } : item).ToArray() });
        if (condition == "unfinished_video") fixture.NodeRows.Single(item => item.NodeType == "generate-video").Status = "cancelled";
        if (condition == "completed_run") run.Status = "completed";
        var originalPayload = job.Payload;

        var result = await fixture.RunService.ActionAsync(run.Id, new(7, "regenerate_ai_shot", Config: config,
            AiShotQuoteToken: "confirmed", SceneOrder: condition == "all_scenes" ? null : 0), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(expectedCode);
        run.Status.Should().Be(condition == "completed_run" ? "completed" : "cancelled");
        job.Payload.Should().Be(originalPayload);
        fixture.VideoRows.Should().ContainSingle();
        fixture.Quotes.Verify(item => item.VerifyAsync(It.IsAny<WorkflowRun>(), It.IsAny<GenerateVideoConfig>(),
            It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task<(WorkflowRun Run, GenerateVideoConfig Config, MediaJob Job)> PrepareCancelledAiVideoAsync(WorkflowTestData fixture)
    {
        var run = PrepareReview(fixture);
        var source = fixture.AssetRows.Single();
        var config = new GenerateVideoConfig(Mode: "ai_shot", DurationSeconds: 8,
            AiScenes: [new(MockupId: source.Id, DurationSeconds: 4, RightsConfirmed: true),
                new(MockupId: source.Id, DurationSeconds: 4, RightsConfirmed: true)]);
        var board = (await fixture.Runtime.PlanAsync(run, config, default)).Value;
        var generate = fixture.NodeRows.Single(item => item.NodeType == "generate-video");
        generate.InputSnapshot = WorkflowJson.Write(config);
        var job = fixture.AddJob(run);
        job.Status = "succeeded";
        job.Payload = WorkflowJson.Write(new AiRenderJobPayload(fixture.VideoRows.Single().Id, board,
            board.AiShots!.Select(shot => new AiShotJobState(shot.SceneOrder, shot.Fingerprint,
                StorageKey: $"private/scene-{shot.SceneOrder}.mp4", StorageVersion: "1", Status: "stored")).ToArray()));
        generate.Status = "succeeded";
        fixture.NodeRows.Single(item => item.NodeType == "review-video").Status = "cancelled";
        fixture.NodeRows.Single(item => item.NodeType == "export-zip").Status = "cancelled";
        run.Status = "cancelled";
        run.CompletedAt = WorkflowTestData.Now.UtcDateTime;
        return (run, config, job);
    }

    internal static WorkflowRun PrepareReview(WorkflowTestData fixture)
    {
        var product = fixture.AddProduct();
        var asset = fixture.AddAsset(product);
        var run = fixture.AddRun(product, "waiting_for_review");
        foreach (var node in fixture.NodeRows.Where(x => x.NodeType is not ("review-video" or "export-zip"))) node.Status = "succeeded";
        var video = new PromoVideo { Id = Guid.NewGuid(), ProductId = product.Id, WorkflowRunId = run.Id, Status = "completed", Mode = "standard",
            ApprovalStatus = "pending", ReviewRevision = 2, VersionNumber = 1, SeriesId = Guid.NewGuid(), StorageKey = "private/approved-video", StorageVersion = "1",
            ThumbnailStorageKey = "private/thumbnail", ThumbnailStorageVersion = "1", Fingerprint = "fixed-fingerprint" };
        fixture.VideoRows.Add(video);
        var review = fixture.NodeRows.Single(x => x.NodeType == "review-video"); review.Status = "waiting_for_review"; review.OutputSnapshot = WorkflowJson.Write(new VideoPointer(video.Id));
        fixture.NodeRows.Single(x => x.NodeType == "generate-video").OutputSnapshot = review.OutputSnapshot;
        fixture.SceneRows.Add(new() { Id = Guid.NewGuid(), PromoVideoId = video.Id, MockupImageId = asset.Id, SourceRevision = 3,
            SourceHash = asset.ContentHash, GenerationStrategy = "standard", SceneOrder = 0 });
        return run;
    }
}
