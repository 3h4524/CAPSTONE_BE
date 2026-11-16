using System.Text.Json;
using APCS.Application.Abstractions.BackgroundJobs;
using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Common;
using APCS.Application.Features.Shops.Dtos.Request;
using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Workflows;

[TestClass]
public sealed class WorkflowPublishServiceTests
{
    private readonly WorkflowTestData fixture = new();
    private readonly Mock<IWorkflowPublishQueue> queue = new();
    private readonly Mock<IPrintifyService> printify = new();
    private readonly Mock<IEtsyPublishingService> etsy = new();
    private readonly Mock<IListingPace> pace = new();
    private readonly Mock<IPrintifyMockupMatcher> matcher = new();
    private readonly Guid designId = Guid.NewGuid();
    private readonly Guid mockupId = Guid.NewGuid();
    private WorkflowRuntime runtime = null!;
    private WorkflowRun run = null!;

    private static readonly object PrintifySettings = new { blueprintId = 6, blueprintTitle = "Softstyle Tee", printProviderId = 99, colors = new[] { "White" }, sizes = new[] { "S", "M" }, targetProfit = 10 };
    private static readonly object EtsySettings = new { listingType = "download", price = 4.99, taxonomyId = 2078, taxonomyPath = "Craft Supplies" };

    /// <summary>A video run whose package is exported, followed by the given publish steps (all pending).</summary>
    private WorkflowPublishService Create(params (string Type, object Config)[] steps)
    {
        var product = fixture.AddProduct();
        run = fixture.AddRun(product);
        foreach (var node in fixture.NodeRows) node.Status = "succeeded";
        var definition = WorkflowJson.Read<WorkflowDefinition>(run.DefinitionSnapshot);
        var publishNodes = steps.Select((step, i) => new WorkflowNode("publish-" + i, step.Type, step.Type, new(), JsonSerializer.SerializeToElement(step.Config))).ToArray();
        run.DefinitionSnapshot = WorkflowJson.Write(definition with { Nodes = definition.Nodes.Concat(publishNodes).ToArray() });
        foreach (var node in publishNodes)
            fixture.NodeRows.Add(new WorkflowNodeRun
            {
                Id = Guid.NewGuid(), WorkflowRunId = run.Id, NodeId = node.Id, NodeType = node.Type, Status = "pending", Attempt = 1,
                InputSnapshot = node.Config.GetRawText(), OutputSnapshot = "{}", CreatedAt = WorkflowTestData.Now.UtcDateTime, UpdatedAt = WorkflowTestData.Now.UtcDateTime,
            });
        foreach (var node in fixture.NodeRows) node.WorkflowRun = run;
        fixture.Nodes.Setup(x => x.Query()).Returns(() => fixture.NodeRows.AsQueryable().BuildMock());

        runtime = new WorkflowRuntime(fixture.Nodes.Object, fixture.Products.Object, fixture.Assets.Object, fixture.Videos.Object, fixture.Scenes.Object,
            fixture.Templates.Object, fixture.Jobs.Object, fixture.Exports.Object, fixture.Items.Object, new(), fixture.ListingContent.Object, fixture.Time,
            publishQueue: queue.Object);

        printify.Setup(x => x.GetDraftAsync(run.ProductId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new PrintifyDraftResponseDto(
            run.ProductId, "Cat tee", new PrintifyConnectionDto("connected", 777, "APCSCo", "etsy", "key", null, 0), "Cat Tee", "A soft tee.", ["cat"], [],
            [new ShopDesignDto(designId, "https://cdn.test/d.png", 4500, 5400, "png", 3, true)],
            [new EtsyDraftMockupDto(mockupId, "https://cdn.test/m.png", 1080, 1080)],
            new PrintifyProductSettingsDto(null, null, null, null, [], [], 24.99m), [], true)));
        printify.Setup(x => x.GetCatalogAsync(6, 99, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new PrintifyCatalogDto(6, 99,
            [new(101, "White / S", "White", "S"), new(102, "White / M", "White", "M"), new(103, "Black / S", "Black", "S")],
            ["White", "Black"], ["S", "M"], ["front"], 4.75m, 2.40m)));
        printify.Setup(x => x.CreateProductAsync(run.ProductId, It.IsAny<CreatePrintifyProductRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Product("created")));
        printify.Setup(x => x.PublishAsync(run.ProductId, "p-1", It.IsAny<PublishPrintifyProductRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Product("publishing")));
        printify.Setup(x => x.SyncAsync(run.ProductId, "p-1", It.IsAny<SyncPrintifyProductRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Product("publishing")));

        return new WorkflowPublishService(fixture.Nodes.Object, fixture.Runs.Object, fixture.State.Object, fixture.Unit.Object, runtime,
            printify.Object, etsy.Object, fixture.Time, NullLogger<WorkflowPublishService>.Instance, pace.Object, matcher.Object, social: socialKits.Object, pinterest: pins.Object);
    }

    private PrintifyProductDto Product(string state, string? listingId = null) => new(run.ProductId, "p-1", "Cat Tee", state, listingId,
        listingId is null ? null : $"https://www.etsy.com/listing/{listingId}",
        [new(101, "White / S", 10.10m, 24.99m, true, 3.28m, 11.61m), new(102, "White / M", 10.10m, 24.99m, true, 3.28m, 11.61m)],
        4.75m, false, listingId is null ? 0 : 1, []);

    private WorkflowNodeRun Step(string type) => fixture.NodeRows.Single(x => x.NodeType == type);

    private readonly Mock<APCS.Application.Features.Social.ISocialShareKitService> socialKits = new();
    private readonly Mock<APCS.Application.Features.Social.IPinterestService> pins = new();

    /// <summary>A kit with an image pin per mock-up, a video pin and a TikTok post.</summary>
    private void PinKit(params Guid[] mockups) => socialKits.Setup(x => x.BuildAsync(run.ProductId, run.UserId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success(new APCS.Application.Features.Social.SocialShareKitDto(run.ProductId, "Cat tee", "https://www.etsy.com/listing/1",
            [
                .. mockups.Select(id => new APCS.Application.Features.Social.SocialPostDto("pinterest", "image",
                    [new APCS.Application.Features.Social.SocialMediaDto("image", $"https://cdn.test/{id}.jpg", "https://cdn.test/dl.jpg", 1000, 1500, "pin.jpg", id)],
                    "Cat Tee", "A soft tee.", [], "https://www.etsy.com/listing/1", "A cat tee")),
                new APCS.Application.Features.Social.SocialPostDto("pinterest", "video", [], "Cat Tee", "A soft tee.", [], "https://www.etsy.com/listing/1", null),
                new APCS.Application.Features.Social.SocialPostDto("tiktok", "video", [], null, "caption", [], null, null),
            ], [])));

    [TestMethod]
    public async Task ProcessAsync_PushToMediaWithABoard_PostsTheImagePinsToIt()
    {
        var service = Create(("publish-social", new { pinterest = true, tiktok = true, boardId = "b-1", boardName = "Cat tees" }));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        PinKit(first, second);
        IReadOnlyList<APCS.Application.Features.Social.Common.PinToPost>? sent = null;
        pins.Setup(x => x.PostPinsAsync(run.UserId, run.ProductId, "b-1", It.IsAny<IReadOnlyList<APCS.Application.Features.Social.Common.PinToPost>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, string, IReadOnlyList<APCS.Application.Features.Social.Common.PinToPost>, CancellationToken>((_, _, _, list, _) => sent = list)
            .ReturnsAsync(Result.Success(new APCS.Application.Features.Social.Common.PostPinsResult(
                [new(first, "1", "https://www.pinterest.com/pin/1/", false), new(second, "2", "https://www.pinterest.com/pin/2/", true)], [])));
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-social").Id, default);

        Step("publish-social").Status.Should().Be("succeeded");
        sent!.Select(pin => pin.MockupImageId).Should().Equal(first, second);
        sent![0].ImageUrl.Should().Be($"https://cdn.test/{first}.jpg");
        sent![0].Link.Should().Be("https://www.etsy.com/listing/1");
        var state = WorkflowJson.Read<PublishNodeState>(Step("publish-social").OutputSnapshot);
        state.PinsPosted.Should().Be(2);
        state.PinUrls.Should().Equal("https://www.pinterest.com/pin/1/", "https://www.pinterest.com/pin/2/");
        state.BoardName.Should().Be("Cat tees");
        state.Warnings.Should().Contain(item => item.Contains("video pin"));
    }

    [TestMethod]
    public async Task ProcessAsync_PushToMediaWithoutABoard_LeavesThePinsToPostByHand()
    {
        var service = Create(("publish-social", new { pinterest = true, tiktok = false }));
        PinKit(Guid.NewGuid());
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-social").Id, default);

        Step("publish-social").Status.Should().Be("succeeded");
        var state = WorkflowJson.Read<PublishNodeState>(Step("publish-social").OutputSnapshot);
        state.PinsPosted.Should().Be(0);
        state.Warnings.Should().Contain(item => item.Contains("Choose a Pinterest board"));
        pins.Verify(x => x.PostPinsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<APCS.Application.Features.Social.Common.PinToPost>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessAsync_PushToMediaWithALostPinterestGrant_FailsAskingToConnectAgain()
    {
        var service = Create(("publish-social", new { pinterest = true, tiktok = false, boardId = "b-1" }));
        PinKit(Guid.NewGuid());
        pins.Setup(x => x.PostPinsAsync(run.UserId, run.ProductId, "b-1", It.IsAny<IReadOnlyList<APCS.Application.Features.Social.Common.PinToPost>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<APCS.Application.Features.Social.Common.PostPinsResult>(APCS.Application.Features.Social.Common.SocialErrors.PinterestReconnectRequired()));
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-social").Id, default);

        Step("publish-social").Status.Should().Be("failed");
        Step("publish-social").ErrorMessage.Should().Contain("Connect your Pinterest account again");
    }

    private void Kit(params string[] platforms) => socialKits.Setup(x => x.BuildAsync(run.ProductId, run.UserId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success(new APCS.Application.Features.Social.SocialShareKitDto(run.ProductId, "Cat tee", "https://www.etsy.com/listing/1",
            platforms.Select(platform => new APCS.Application.Features.Social.SocialPostDto(platform, "image", [], null, "caption", [], null, null)).ToArray(),
            platforms.Length == 0 ? ["The product has no approved mock-up yet."] : [])));

    [TestMethod]
    public async Task ProcessAsync_PushToMedia_PreparesThePostsOfTheChosenPlatforms_AndCompletesTheRun()
    {
        var service = Create(("publish-social", new { platforms = new[] { "pinterest" } }));
        Kit("pinterest", "pinterest", "tiktok");
        await runtime.AdvanceAsync(run, default);

        (await service.ProcessAsync(Step("publish-social").Id, default)).Should().BeNull();

        Step("publish-social").Status.Should().Be("succeeded");
        var state = WorkflowJson.Read<PublishNodeState>(Step("publish-social").OutputSnapshot);
        state.SocialPosts.Should().Be(2);
        state.Platforms.Should().Equal("pinterest");
        run.Status.Should().Be("completed");
        pace.Verify(x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never, "posts are not listings");
    }

    [TestMethod]
    public async Task ProcessAsync_PushToMedia_ReadsThePlatformSwitchesOfTheCanvas()
    {
        var service = Create(("publish-social", new { pinterest = false, tiktok = true }));
        Kit("pinterest", "tiktok", "tiktok");
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-social").Id, default);

        var state = WorkflowJson.Read<PublishNodeState>(Step("publish-social").OutputSnapshot);
        state.Platforms.Should().Equal("tiktok");
        state.SocialPosts.Should().Be(2);
    }

    [TestMethod]
    public async Task ProcessAsync_PushToMediaWithNothingToPost_FailsSayingWhatToApprove()
    {
        var service = Create(("publish-social", new { }));
        Kit();
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-social").Id, default);

        Step("publish-social").Status.Should().Be("failed");
        Step("publish-social").ErrorMessage.Should().Contain("nothing to post").And.Contain("approved mock-up");
    }

    // Push to Printify runs on its own beside the line, so Publish to Etsy does not wait for it.
    [TestMethod]
    public async Task AdvanceAsync_PackageExported_QueuesPrintifyAndEtsyEachOnItsOwn()
    {
        Create(("publish-printify", PrintifySettings), ("publish-etsy", EtsySettings));

        await runtime.AdvanceAsync(run, default);

        Step("publish-printify").Status.Should().Be("running");
        Step("publish-etsy").Status.Should().Be("running");
        run.Status.Should().Be("running");
        queue.Verify(x => x.Enqueue(Step("publish-printify").Id, null), Times.Once);
        queue.Verify(x => x.Enqueue(Step("publish-etsy").Id, null), Times.Once);
    }

    // Printify failing does not stop the video: only its step fails while the video part is still at work.
    [TestMethod]
    public async Task ProcessAsync_PrintifyFailsWhileTheVideoRenders_LeavesTheVideoGoing()
    {
        var service = Create(("publish-printify", PrintifySettings));
        printify.Setup(x => x.CreateProductAsync(run.ProductId, It.IsAny<CreatePrintifyProductRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PrintifyProductDto>(Error.Failure("shop.printify_rejected", "Printify did not accept it.")));
        await runtime.AdvanceAsync(run, default);
        var generate = Step("generate-video");
        generate.Status = "running";

        await service.ProcessAsync(Step("publish-printify").Id, default);

        Step("publish-printify").Status.Should().Be("failed");
        generate.Status.Should().Be("running");
        run.Status.Should().Be("running");
    }

    // A video that failed does not stop Printify: it carries on and publishes the design and its mock-ups.
    [TestMethod]
    public async Task ProcessAsync_VideoFailed_PrintifyStillPublishes()
    {
        var service = Create(("publish-printify", PrintifySettings));
        await runtime.AdvanceAsync(run, default);
        Step("generate-video").Status = "failed";
        run.Status = "failed";

        var wait = await service.ProcessAsync(Step("publish-printify").Id, default);

        wait.Should().Be(WorkflowPublishService.PollEvery);
        printify.Verify(x => x.CreateProductAsync(run.ProductId, It.IsAny<CreatePrintifyProductRequestDto>(), It.IsAny<CancellationToken>()), Times.Once);
        run.Status.Should().Be("failed", "the video's failure is still the run's to retry");
    }

    [TestMethod]
    public async Task ProcessAsync_PrintifyWhileTheVideoWaitsForReview_IsStillWorkedOn()
    {
        var service = Create(("publish-printify", PrintifySettings));
        await runtime.AdvanceAsync(run, default);
        run.Status = "waiting_for_review";

        (await service.ProcessAsync(Step("publish-printify").Id, default)).Should().Be(WorkflowPublishService.PollEvery);
        run.Status.Should().Be("waiting_for_review");
    }

    [TestMethod]
    public async Task RetryAction_OnlyPrintifyFailed_RunsItAgainWithoutTouchingTheVideo()
    {
        Create(("publish-printify", PrintifySettings));
        var printifyStep = Step("publish-printify");
        printifyStep.Status = "failed"; printifyStep.ErrorMessage = "Printify was down.";
        var review = Step("review-video");
        review.Status = "waiting_for_review";
        run.Status = "waiting_for_review";

        var result = await fixture.RunService.ActionAsync(run.Id, new(run.Revision, "retry"), default);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        printifyStep.Status.Should().Be("running");
        printifyStep.Attempt.Should().Be(2);
        run.Status.Should().Be("waiting_for_review");
    }

    [TestMethod]
    public async Task ProcessAsync_Printify_PricesEachVariantForTheTargetProfit_AndWaitsForEtsy()
    {
        var service = Create(("publish-printify", PrintifySettings));
        await runtime.AdvanceAsync(run, default);

        var wait = await service.ProcessAsync(Step("publish-printify").Id, default);

        wait.Should().Be(WorkflowPublishService.PollEvery);
        printify.Verify(x => x.CreateProductAsync(run.ProductId, It.Is<CreatePrintifyProductRequestDto>(request =>
            request.DesignId == designId && request.VariantIds.SequenceEqual(new long[] { 101, 102 }) && request.PlacementMockupId == null), It.IsAny<CancellationToken>()), Times.Once);
        var expected = PrintifyPricing.PriceForProfit(10.10m, 4.75m, 10m);
        printify.Verify(x => x.PublishAsync(run.ProductId, "p-1", It.Is<PublishPrintifyProductRequestDto>(request =>
            request.Prices.All(price => price.Price == expected)), It.IsAny<CancellationToken>()), Times.Once);
        Step("publish-printify").Status.Should().Be("running");
        WorkflowJson.Read<PublishNodeState>(Step("publish-printify").OutputSnapshot).Stage.Should().Be("publishing");
    }

    [TestMethod]
    public async Task ProcessAsync_Printify_OnceOnEtsy_AddsTheMockupsAndCompletesTheRun()
    {
        var service = Create(("publish-printify", PrintifySettings));
        await runtime.AdvanceAsync(run, default);
        await service.ProcessAsync(Step("publish-printify").Id, default);
        printify.Setup(x => x.SyncAsync(run.ProductId, "p-1", It.IsAny<SyncPrintifyProductRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Product("published", "4455")));

        var wait = await service.ProcessAsync(Step("publish-printify").Id, default);

        wait.Should().BeNull();
        printify.Verify(x => x.SyncAsync(run.ProductId, "p-1", It.Is<SyncPrintifyProductRequestDto>(media => media.MockupIds.Contains(mockupId)), It.IsAny<CancellationToken>()));
        Step("publish-printify").Status.Should().Be("succeeded");
        WorkflowJson.Read<PublishNodeState>(Step("publish-printify").OutputSnapshot).EtsyUrl.Should().Be("https://www.etsy.com/listing/4455");
        run.Status.Should().Be("completed");
    }

    [TestMethod]
    public async Task ProcessAsync_Printify_TooLongWithoutAListing_FailsTheStep()
    {
        var service = Create(("publish-printify", PrintifySettings));
        await runtime.AdvanceAsync(run, default);
        await service.ProcessAsync(Step("publish-printify").Id, default);
        fixture.Time.Advance(WorkflowPublishService.GiveUpAfter + TimeSpan.FromMinutes(1));

        (await service.ProcessAsync(Step("publish-printify").Id, default)).Should().BeNull();

        Step("publish-printify").Status.Should().Be("failed");
        run.Status.Should().Be("failed");
        run.ErrorMessage.Should().Contain("15 minutes");
    }

    [TestMethod]
    public async Task ProcessAsync_PrintifyRefuses_FailsTheRunWithItsReason()
    {
        var service = Create(("publish-printify", PrintifySettings));
        printify.Setup(x => x.CreateProductAsync(run.ProductId, It.IsAny<CreatePrintifyProductRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PrintifyProductDto>(Error.Failure("shop.printify_rejected", "Printify did not accept it: Image has low quality")));
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-printify").Id, default);

        Step("publish-printify").Status.Should().Be("failed");
        run.ErrorMessage.Should().Contain("Image has low quality");
    }

    [TestMethod]
    public async Task ProcessAsync_TheShopReachedItsListingPace_WaitsWithoutSendingAnything()
    {
        var service = Create(("publish-printify", PrintifySettings));
        var retryAt = WorkflowTestData.Now.UtcDateTime.AddMinutes(40);
        pace.Setup(x => x.CheckAsync(run.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new ListingPaceDecision(false, retryAt, "pace"));
        await runtime.AdvanceAsync(run, default);

        var wait = await service.ProcessAsync(Step("publish-printify").Id, default);

        wait.Should().Be(TimeSpan.FromMinutes(40));
        Step("publish-printify").Status.Should().Be("running");
        Step("publish-printify").Stage.Should().Contain("listing pace");
        printify.Verify(x => x.CreateProductAsync(It.IsAny<Guid>(), It.IsAny<CreatePrintifyProductRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessAsync_PrintifyOnly_DoesNotWaitForTheShopsListingPace()
    {
        var service = Create(("publish-printify", new { blueprintId = 6, blueprintTitle = "Softstyle Tee", printProviderId = 99,
            colors = new[] { "White" }, sizes = new[] { "S", "M" }, targetProfit = 10, publishToEtsy = false }));
        pace.Setup(x => x.CheckAsync(run.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListingPaceDecision(false, WorkflowTestData.Now.UtcDateTime.AddMinutes(40), "pace"));
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-printify").Id, default);

        Step("publish-printify").Stage.Should().NotContain("listing pace");
        pace.Verify(x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        printify.Verify(x => x.CreateProductAsync(It.IsAny<Guid>(), It.IsAny<CreatePrintifyProductRequestDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessAsync_StoppedWhilePrintifyMadeTheProduct_RecordsTheProductMade()
    {
        var service = Create(("publish-printify", new { blueprintId = 6, blueprintTitle = "Softstyle Tee", printProviderId = 99,
            colors = new[] { "White" }, sizes = new[] { "S", "M" }, targetProfit = 10, publishToEtsy = false }));
        await runtime.AdvanceAsync(run, default);
        // The seller presses Stop while Printify is making the product.
        printify.Setup(x => x.CreateProductAsync(run.ProductId, It.IsAny<CreatePrintifyProductRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback(() => { run.Status = "cancelled"; Step("publish-printify").Status = "cancelled"; })
            .ReturnsAsync(Result.Success(Product("created")));

        await service.ProcessAsync(Step("publish-printify").Id, default);

        Step("publish-printify").Status.Should().Be("succeeded");
        run.Status.Should().Be("cancelled");
    }

    private void EtsyDraftWith(params EtsyDraftMockupDto[] mockups) =>
        etsy.Setup(x => x.GetDraftAsync(run.ProductId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new EtsyDraftResponseDto(
            run.ProductId, "Cat tee", ShopConnectionStatuses.Connected, "APCSCo", "USD", "Cat Tee", "A tee.", ["cat"], [], mockups, null,
            new EtsyListingSettingsDto(null, 999, null, null, "physical", "i_did", "made_to_order", null, null, []), [], [], [], [], [])));

    private static readonly object PhysicalSettings = new
    {
        listingType = "physical", price = 24.99, taxonomyId = 482, taxonomyPath = "T-shirts", shippingProfileId = 55, madeBy = "partner", productionPartnerIds = new[] { 7L },
    };

    [TestMethod]
    public async Task ProcessAsync_EtsyPhysical_OpensOnAPhotoNotMadeWithAi_AndNamesThePartner()
    {
        var service = Create(("publish-etsy", PhysicalSettings));
        var real = Guid.NewGuid();
        EtsyDraftWith(new EtsyDraftMockupDto(mockupId, "https://cdn.test/ai.png", 1080, 1080, AiMade: true), new EtsyDraftMockupDto(real, "https://cdn.test/m.png", 1080, 1080));
        etsy.Setup(x => x.PublishDraftAsync(run.ProductId, It.IsAny<PublishEtsyDraftRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishEtsyDraftResponseDto("987", "https://www.etsy.com/your/shops/me/listing-editor/edit/987", 2, false, false, [], 0)));
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-etsy").Id, default);

        etsy.Verify(x => x.PublishDraftAsync(run.ProductId, It.Is<PublishEtsyDraftRequestDto>(request =>
            request.MockupIds.SequenceEqual(new[] { real, mockupId }) && request.ProductionPartnerIds.SequenceEqual(new long[] { 7 })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessAsync_EtsyPhysicalWithOnlyAiPhotos_FailsTheStep()
    {
        var service = Create(("publish-etsy", PhysicalSettings));
        EtsyDraftWith(new EtsyDraftMockupDto(mockupId, "https://cdn.test/ai.png", 1080, 1080, AiMade: true));
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-etsy").Id, default);

        Step("publish-etsy").Status.Should().Be("failed");
        Step("publish-etsy").ErrorMessage.Should().Contain("made with AI");
        etsy.Verify(x => x.PublishDraftAsync(It.IsAny<Guid>(), It.IsAny<PublishEtsyDraftRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void EtsyPublishConfig_APhysicalItem_MustSayWhoMakesIt_AndNameThePartner()
    {
        var physical = new EtsyPublishConfig("physical", 24.99m, 999, 2078, null, "i_did", "made_to_order", 55, true);

        physical.Problem().Should().Contain("who makes this item");
        (physical with { MadeBy = "partner" }).Problem().Should().Contain("production partner");
        (physical with { MadeBy = "partner", ProductionPartnerIds = [7] }).Problem().Should().BeNull();
        (physical with { MadeBy = "partner", ProductionPartnerIds = [7] }).Partners.Should().Equal(7);
        (physical with { MadeBy = "me", ProductionPartnerIds = [7] }).Partners.Should().BeEmpty();
        // A download is made by no one.
        (physical with { ListingType = "download", ShippingProfileId = null }).Problem().Should().BeNull();
    }

    [TestMethod]
    public async Task ProcessAsync_EtsyDigital_SendsTheDesignFiles_AndCompletesTheRun()
    {
        var service = Create(("publish-etsy", EtsySettings));
        etsy.Setup(x => x.GetDraftAsync(run.ProductId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(new EtsyDraftResponseDto(
            run.ProductId, "Cat tee", ShopConnectionStatuses.Connected, "APCSCo", "USD", "Cat PNG", "A file.", ["cat"], [],
            [new EtsyDraftMockupDto(mockupId, "https://cdn.test/m.png", 1080, 1080)], null,
            new EtsyListingSettingsDto(null, 999, null, null, "download", "i_did", "made_to_order", null, null, []), [], [], [], [],
            [new ShopDesignDto(designId, "https://cdn.test/d.png", 4500, 5400, "png", 3, true)])));
        etsy.Setup(x => x.PublishDraftAsync(run.ProductId, It.IsAny<PublishEtsyDraftRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishEtsyDraftResponseDto("987", "https://www.etsy.com/your/shops/me/listing-editor/edit/987", 1, false, false, [], 1)));
        await runtime.AdvanceAsync(run, default);

        (await service.ProcessAsync(Step("publish-etsy").Id, default)).Should().BeNull();

        etsy.Verify(x => x.PublishDraftAsync(run.ProductId, It.Is<PublishEtsyDraftRequestDto>(request =>
            request.Type == "download" && request.Price == 4.99m && request.DesignIds!.Single() == designId && request.ShippingProfileId == null),
            It.IsAny<CancellationToken>()), Times.Once);
        Step("publish-etsy").Status.Should().Be("succeeded");
        run.Status.Should().Be("completed");
    }

    [TestMethod]
    public async Task ProcessAsync_RunCancelledMeanwhile_WritesNothing()
    {
        var service = Create(("publish-printify", PrintifySettings));
        await runtime.AdvanceAsync(run, default);
        run.Status = "cancelled";

        (await service.ProcessAsync(Step("publish-printify").Id, default)).Should().BeNull();

        printify.Verify(x => x.CreateProductAsync(It.IsAny<Guid>(), It.IsAny<CreatePrintifyProductRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RetryAction_FailedPublishStep_RunsOnlyThatStepAgain()
    {
        var service = Create(("publish-printify", PrintifySettings));
        var step = Step("publish-printify");
        step.Status = "failed";
        run.Status = "failed";
        run.ErrorMessage = "Printify did not accept it.";

        var result = await fixture.RunService.ActionAsync(run.Id, new RunActionRequest(run.Revision, "retry"), default);

        result.IsSuccess.Should().BeTrue();
        step.Status.Should().Be("running");
        step.Attempt.Should().Be(2);
        run.Status.Should().Be("running");
        fixture.NodeRows.Single(x => x.NodeType == "generate-video").Status.Should().Be("succeeded");
    }

    [TestMethod]
    public void PriceForProfit_LeavesAtLeastTheTargetAfterFees()
    {
        var price = PrintifyPricing.PriceForProfit(10.10m, 4.75m, 10m);

        price.Should().Be(23.99m);
        PrintifyPricing.EstimatedProfit(price, 10.10m, 4.75m).Should().BeGreaterThanOrEqualTo(10m);
    }

    /// <summary>Sets the run's Apply mock-up node to the given Printify products, by product type.</summary>
    private void MockupFor(Dictionary<string, object> products)
    {
        var definition = WorkflowJson.Read<WorkflowDefinition>(run.DefinitionSnapshot);
        var config = JsonSerializer.SerializeToElement(new { printifyProducts = products });
        var nodes = definition.Nodes.Any(node => node.Type == "apply-mockup")
            ? definition.Nodes.Select(node => node.Type == "apply-mockup" ? node with { Config = config } : node).ToArray()
            : definition.Nodes.Append(new WorkflowNode("mockup", "apply-mockup", "Apply mock-up", new(), config)).ToArray();
        run.DefinitionSnapshot = WorkflowJson.Write(definition with { Nodes = nodes });
    }

    [TestMethod]
    public async Task ProcessAsync_ApplyMockupChoseThePrintifyProduct_CreatesThatProductInItsPrintArea()
    {
        var service = Create(("publish-printify", new { targetProfit = 10 }));
        MockupFor(new()
        {
            ["tshirt"] = new { blueprintId = 6, blueprintTitle = "Softstyle Tee", printProviderId = 99, colors = new[] { "Black" }, sizes = new[] { "S" }, position = "front" },
        });
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-printify").Id, default);

        printify.Verify(x => x.CreateProductAsync(run.ProductId, It.Is<CreatePrintifyProductRequestDto>(request =>
            request.BlueprintId == 6 && request.PrintProviderId == 99 && request.VariantIds.SequenceEqual(new long[] { 103 }) && request.Position == "front"
            // The print follows the listing's first mock-up, and the listing opens on Printify's photo of the garment.
            && request.PlacementMockupId == mockupId),
            It.IsAny<CancellationToken>()), Times.Once);
        printify.Verify(x => x.SyncAsync(run.ProductId, "p-1", It.Is<SyncPrintifyProductRequestDto>(media => media.AfterPrintifyPhoto), It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task ProcessAsync_ApplyMockupChoseThePrintifyProduct_TheSellerCanLeadWithTheirMockup()
    {
        var service = Create(("publish-printify", new { targetProfit = 10, leadPhoto = "apcs" }));
        MockupFor(new()
        {
            ["tshirt"] = new { blueprintId = 6, blueprintTitle = "Softstyle Tee", printProviderId = 99, colors = new[] { "Black" }, sizes = new[] { "S" } },
        });
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-printify").Id, default);

        printify.Verify(x => x.SyncAsync(run.ProductId, "p-1", It.Is<SyncPrintifyProductRequestDto>(media => !media.AfterPrintifyPhoto), It.IsAny<CancellationToken>()));
    }

    [TestMethod]
    public async Task ProcessAsync_ApplyMockupHasNoPrintifyProductForTheType_FailsTheStepNamingIt()
    {
        var service = Create(("publish-printify", PrintifySettings));
        MockupFor(new()
        {
            ["mug"] = new { blueprintId = 68, printProviderId = 1, colors = new[] { "White" }, sizes = new[] { "11oz" } },
        });
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-printify").Id, default);

        Step("publish-printify").Status.Should().Be("failed");
        Step("publish-printify").ErrorMessage.Should().Contain("tshirt");
        printify.Verify(x => x.CreateProductAsync(It.IsAny<Guid>(), It.IsAny<CreatePrintifyProductRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessAsync_Printify_ProductReadFromTheMockups_IsMadeAsTheyShow()
    {
        var service = Create(("publish-printify", new { source = "mockup", targetProfit = 10 }));
        matcher.Setup(x => x.MatchAsync(run.ProductId, It.IsAny<PrintifyPublishConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<PrintifyMockupMatch?>(new PrintifyMockupMatch(
                new PrintifyProductSetup(6, "Softstyle Tee", 99, "Monster Digital", ["White"], ["S", "M"], "front"), "Softstyle Tee",
                ["No Printify color is set for #123456; left out."])));
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-printify").Id, default);

        printify.Verify(x => x.CreateProductAsync(run.ProductId, It.Is<CreatePrintifyProductRequestDto>(request =>
            request.BlueprintId == 6 && request.PrintProviderId == 99 && request.VariantIds.SequenceEqual(new long[] { 101, 102 })
            && request.PlacementMockupId == mockupId), It.IsAny<CancellationToken>()), Times.Once);
        var state = WorkflowJson.Read<PublishNodeState>(Step("publish-printify").OutputSnapshot);
        state.MadeAs.Should().Be("Softstyle Tee by Monster Digital in White (S, M)");
        state.Warnings.Should().Contain("No Printify color is set for #123456; left out.");
        state.MockupsAfterPrintify.Should().BeTrue();
    }

    [TestMethod]
    public async Task ProcessAsync_Printify_TheSellersOwnTemplatesWithoutAProduct_FailsAndAsksForOne()
    {
        var service = Create(("publish-printify", new { targetProfit = 10 }));
        matcher.Setup(x => x.MatchAsync(run.ProductId, It.IsAny<PrintifyPublishConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<PrintifyMockupMatch?>(null));
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-printify").Id, default);

        Step("publish-printify").Status.Should().Be("failed");
        Step("publish-printify").ErrorMessage.Should().Contain("your own templates");
        printify.Verify(x => x.CreateProductAsync(It.IsAny<Guid>(), It.IsAny<CreatePrintifyProductRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessAsync_Printify_ManualSource_DoesNotReadTheMockups()
    {
        var service = Create(("publish-printify", new { source = "manual", blueprintId = 6, blueprintTitle = "Softstyle Tee", printProviderId = 99,
            colors = new[] { "White" }, sizes = new[] { "S" }, targetProfit = 10 }));
        await runtime.AdvanceAsync(run, default);

        await service.ProcessAsync(Step("publish-printify").Id, default);

        matcher.Verify(x => x.MatchAsync(It.IsAny<Guid>(), It.IsAny<PrintifyPublishConfig>(), It.IsAny<CancellationToken>()), Times.Never);
        printify.Verify(x => x.CreateProductAsync(run.ProductId, It.Is<CreatePrintifyProductRequestDto>(request =>
            request.VariantIds.SequenceEqual(new long[] { 101 })), It.IsAny<CancellationToken>()), Times.Once);
    }
}
