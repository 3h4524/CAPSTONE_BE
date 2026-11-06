using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.SellerDashboard;
using APCS.Application.Features.SellerDashboard.Common;
using APCS.Application.Features.SellerDashboard.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MockQueryable.Moq;
using Moq;
using ListingContentEntity = APCS.Domain.Entities.ListingContent;

namespace APCS.Application.UnitTests.Features.SellerDashboard;

[TestClass]
public sealed class SellerDashboardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Seller = Guid.NewGuid();
    private static readonly Guid OtherSeller = Guid.NewGuid();

    private sealed class Data
    {
        public List<Product> Products { get; } = [];
        public List<Batch> Batches { get; } = [];
        public List<BatchJob> Jobs { get; } = [];
        public List<BatchJobProduct> JobProducts { get; } = [];
        public List<PromoVideo> Videos { get; } = [];
        public List<Workflow> Workflows { get; } = [];
        public List<WorkflowRun> Runs { get; } = [];
        public List<ListingContentEntity> Listings { get; } = [];
        public List<SeoScore> SeoScores { get; } = [];
        public List<EtsyIntegration> Integrations { get; } = [];
        public List<EtsyUploadLog> Uploads { get; } = [];
        public List<ApiUsageRecord> Calls { get; } = [];
    }

    private static SellerDashboardService CreateService(Data data, Guid? userId = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(userId ?? Seller);
        return CreateService(data, currentUser.Object);
    }

    private static SellerDashboardService CreateService(Data data, ICurrentUser currentUser) => new(
        currentUser, Repo(data.Products), Repo(data.Batches), Repo(data.Jobs), Repo(data.JobProducts), Repo(data.Videos),
        Repo(data.Workflows), Repo(data.Runs), Repo(data.Listings), Repo(data.SeoScores), Repo(data.Integrations), Repo(data.Uploads),
        Repo(data.Calls), new FakeTimeProvider(Now));

    private static IRepository<T> Repo<T>(List<T> rows) where T : class
    {
        var repo = new Mock<IRepository<T>>();
        repo.Setup(x => x.Query()).Returns(() => rows.AsQueryable().BuildMock());
        return repo.Object;
    }

    private static DateTime HoursAgo(double hours) => Now.UtcDateTime.AddHours(-hours);

    private static Product Product(string status, Guid? owner = null, double createdHoursAgo = 1) => new()
    {
        Id = Guid.NewGuid(),
        UserId = owner ?? Seller,
        ProcessingStatus = status,
        CreatedAt = HoursAgo(createdHoursAgo),
        UpdatedAt = HoursAgo(createdHoursAgo),
    };

    private static BatchJob Job(string status, Guid? owner = null, double hoursAgo = 1) => new()
    {
        Id = Guid.NewGuid(),
        UserId = owner ?? Seller,
        Name = "Halloween hoodies",
        Status = status,
        CreatedAt = HoursAgo(hoursAgo),
        UpdatedAt = HoursAgo(hoursAgo),
    };

    private static BatchJobProduct JobProduct(BatchJob job, string status, Product? product = null, DateTime? started = null, DateTime? completed = null) => new()
    {
        Id = Guid.NewGuid(),
        BatchJob = job,
        BatchJobId = job.Id,
        Product = product,
        ProductId = product?.Id,
        Status = status,
        StartedAt = started,
        CompletedAt = completed,
    };

    [TestMethod]
    public async Task GetSummaryAsync_SignedOut_ReturnsUnauthorized()
    {
        var result = await CreateService(new Data(), new Mock<ICurrentUser>().Object).GetSummaryAsync(30);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [TestMethod]
    public async Task GetSummaryAsync_NoProducts_IsNewWithAnEmptyPipeline()
    {
        var result = await CreateService(new Data()).GetSummaryAsync(30);

        var summary = result.Value;
        summary.Stage.Should().Be("new");
        summary.Pipeline.Should().HaveCount(PipelineStages.Ordered.Count).And.OnlyContain(stage => stage.Count == 0);
        summary.Kpis.ProductsCreated.ChangePercent.Should().BeNull();
    }

    [TestMethod]
    public async Task GetSummaryAsync_FoldsStatusesIntoStagesAndIgnoresOtherSellers()
    {
        var data = new Data();
        data.Products.AddRange([
            Product("pending"), Product("queued"),
            Product("image_review_required"), Product("image_review_required"), Product("image_review_required"),
            Product("review_required"), Product("approved"), Product("exported"),
            Product("failed"), Product("cancelled"),
            Product("image_review_required", OtherSeller),
        ]);
        var deleted = Product("approved");
        deleted.DeletedAt = HoursAgo(0);
        data.Products.Add(deleted);

        var summary = (await CreateService(data).GetSummaryAsync(30)).Value;

        var counts = summary.Pipeline.ToDictionary(stage => stage.Stage, stage => stage.Count);
        counts[PipelineStages.Queued].Should().Be(2);
        counts[PipelineStages.DesignReview].Should().Be(3);
        counts[PipelineStages.Listing].Should().Be(1);
        counts[PipelineStages.Ready].Should().Be(2);
        counts[PipelineStages.Published].Should().Be(0);
        summary.FailedProducts.Should().Be(1);
        summary.Stage.Should().Be("producing");
    }

    [TestMethod]
    public async Task GetSummaryAsync_ComparesThePeriodWithThePeriodBeforeIt()
    {
        var data = new Data();
        // 7-day period: 3 products now, 2 in the 7 days before, 1 older than both.
        data.Products.AddRange([
            Product("approved", createdHoursAgo: 1), Product("approved", createdHoursAgo: 30), Product("approved", createdHoursAgo: 100),
            Product("approved", createdHoursAgo: 8 * 24), Product("approved", createdHoursAgo: 10 * 24),
            Product("approved", createdHoursAgo: 20 * 24),
        ]);
        var job = Job("completed");
        data.Jobs.Add(job);
        // 4 finished this period, 1 of them failed: (4 - 1) / 4 = 75%.
        data.JobProducts.AddRange([
            JobProduct(job, "approved", completed: HoursAgo(2)),
            JobProduct(job, "approved", completed: HoursAgo(3)),
            JobProduct(job, "image_review_required", completed: HoursAgo(4)),
            JobProduct(job, "failed", completed: HoursAgo(5)),
            JobProduct(Job("completed", OtherSeller), "failed", completed: HoursAgo(5)),
        ]);
        // $0.90 over 3 products = $0.30 each.
        data.Calls.AddRange([
            new ApiUsageRecord { UserId = Seller, CostUsd = 0.60m, CreatedAt = HoursAgo(2) },
            new ApiUsageRecord { UserId = Seller, CostUsd = 0.30m, CreatedAt = HoursAgo(3) },
            new ApiUsageRecord { UserId = OtherSeller, CostUsd = 5m, CreatedAt = HoursAgo(3) },
        ]);

        var summary = (await CreateService(data).GetSummaryAsync(7)).Value;

        summary.PeriodDays.Should().Be(7);
        summary.Kpis.ProductsCreated.Value.Should().Be(3);
        summary.Kpis.ProductsCreated.PreviousValue.Should().Be(2);
        summary.Kpis.ProductsCreated.ChangePercent.Should().Be(50);
        summary.Kpis.SuccessRatePercent.Value.Should().Be(75);
        summary.Kpis.CostPerProductUsd.Value.Should().Be(0.30m);
    }

    [TestMethod]
    public async Task GetSummaryAsync_UnsupportedPeriod_ReadsAsThirtyDays()
    {
        var summary = (await CreateService(new Data()).GetSummaryAsync(12)).Value;

        summary.PeriodDays.Should().Be(30);
    }

    [TestMethod]
    public async Task GetSummaryAsync_ListsOnlyUnfinishedBatchJobs()
    {
        var data = new Data();
        data.Products.Add(Product("generating_image"));
        data.Jobs.AddRange([Job("running"), Job("queued"), Job("completed"), Job("running", OtherSeller)]);

        var summary = (await CreateService(data).GetSummaryAsync(30)).Value;

        summary.ActiveBatches.Select(batch => batch.Status).Should().BeEquivalentTo(["running", "queued"]);
        summary.Stage.Should().Be("producing");
    }

    [TestMethod]
    public async Task GetSummaryAsync_ProductRunningPastTheDefaultThreshold_CountsAsStuck()
    {
        var data = new Data();
        var job = Job("running");
        var stuck = Product("generating_video");
        var fresh = Product("generating_video");
        data.Products.AddRange([stuck, fresh]);
        data.Jobs.Add(job);
        // No finished history, so the threshold is the 15-minute default.
        data.JobProducts.AddRange([
            JobProduct(job, "running", stuck, started: HoursAgo(0.5)),
            JobProduct(job, "running", fresh, started: HoursAgo(0.1)),
        ]);

        var summary = (await CreateService(data).GetSummaryAsync(30)).Value;

        var video = summary.Pipeline.Single(stage => stage.Stage == PipelineStages.Video);
        video.Count.Should().Be(2);
        video.StuckCount.Should().Be(1);
    }

    [TestMethod]
    public async Task GetActionItemsAsync_GroupsWorkByKindAndRanksByPriority()
    {
        var data = new Data();
        var job = Job("completed");
        data.Jobs.Add(job);
        // 3 designs waiting 2 hours: 2 × 10 + 3 + 2 = 25.
        data.JobProducts.AddRange(Enumerable.Range(0, 3).Select(_ => JobProduct(job, "image_review_required", completed: HoursAgo(2))));
        // One batch failed 5 hours ago: 3 × 10 + 1 + 5 = 36.
        var failedJob = Job("failed", hoursAgo: 5);
        failedJob.CompletedAt = HoursAgo(5);
        data.Jobs.Add(failedJob);
        // An old failure is history, not work.
        var oldFailure = Job("failed", hoursAgo: 30 * 24);
        oldFailure.CompletedAt = HoursAgo(30 * 24);
        data.Jobs.Add(oldFailure);
        // Another Seller's work is not ours.
        var otherJob = Job("completed", OtherSeller);
        data.JobProducts.Add(JobProduct(otherJob, "image_review_required", completed: HoursAgo(2)));

        var items = (await CreateService(data).GetActionItemsAsync()).Value.Items;

        items.Select(item => item.Type).Should().Equal(ActionItemTypes.BatchFailed, ActionItemTypes.DesignReview);
        items[0].Priority.Should().Be(36);
        items[0].ActionUrl.Should().Be($"/batch-jobs/{failedJob.Id}");
        items[1].Count.Should().Be(3);
        items[1].Priority.Should().Be(25);
        items[1].ActionUrl.Should().Be($"/batch-jobs/{job.Id}");
    }

    [TestMethod]
    public async Task GetActionItemsAsync_FailedVideoLaterRenderedAgain_IsNotReported()
    {
        var data = new Data();
        var retried = Product("approved");
        var broken = Product("approved");
        data.Products.AddRange([retried, broken]);
        data.Videos.AddRange([
            new PromoVideo { ProductId = retried.Id, Product = retried, Status = "failed", ApprovalStatus = "pending", CreatedAt = HoursAgo(3) },
            new PromoVideo { ProductId = retried.Id, Product = retried, Status = "completed", ApprovalStatus = "pending", CreatedAt = HoursAgo(1) },
            new PromoVideo { ProductId = broken.Id, Product = broken, Status = "failed", ApprovalStatus = "pending", CreatedAt = HoursAgo(2) },
        ]);

        var items = (await CreateService(data).GetActionItemsAsync()).Value.Items;

        items.Single(item => item.Type == ActionItemTypes.VideoFailed).Count.Should().Be(1);
        items.Single(item => item.Type == ActionItemTypes.VideoReview).Count.Should().Be(1);
    }

    [TestMethod]
    public async Task GetActionItemsAsync_ExpiredEtsyConnection_ComesBeforeAReviewWaitingLonger()
    {
        var data = new Data();
        data.Integrations.Add(new EtsyIntegration { UserId = Seller, IsActive = true, OauthExpiresAt = HoursAgo(1) });
        var listing = new ListingContentEntity { Product = Product("review_required"), ApprovalStatus = "pending", CreatedAt = HoursAgo(48) };
        data.Listings.Add(listing);
        data.SeoScores.Add(new SeoScore { ListingContent = listing, OverallSeoScore = 42, CalculatedAt = HoursAgo(1) });

        var items = (await CreateService(data).GetActionItemsAsync()).Value.Items;

        items.Select(item => item.Type).Should().Equal(ActionItemTypes.EtsyReconnect, ActionItemTypes.ListingReview, ActionItemTypes.LowSeo);
        items[0].Severity.Should().Be(ActionItemSeverities.Critical);
    }

    [TestMethod]
    public void ActionItemPriority_MatchesTheWorkedExamplesAndCapsTheWait()
    {
        ActionItemPriority.Calculate(ActionItemSeverities.Error, 8, HoursAgo(5), Now.UtcDateTime).Should().Be(43);
        ActionItemPriority.Calculate(ActionItemSeverities.Warning, 12, HoursAgo(2), Now.UtcDateTime).Should().Be(34);
        ActionItemPriority.Calculate(ActionItemSeverities.Info, 1, HoursAgo(24 * 30), Now.UtcDateTime).Should().Be(10 + 1 + 48);
    }

    [TestMethod]
    public void StuckProductRule_IsThreeTimesTheAverageOrFifteenMinutes()
    {
        StuckProductRule.Threshold(90m).Should().Be(TimeSpan.FromSeconds(270));
        StuckProductRule.Threshold(null).Should().Be(TimeSpan.FromMinutes(15));
        StuckProductRule.IsStuck(HoursAgo(0.1), Now.UtcDateTime, TimeSpan.FromSeconds(270)).Should().BeTrue();
    }

    [TestMethod]
    public async Task GetActionItemsAsync_DesignsSpreadOverJobs_LinkEachJobWhereItWasStarted()
    {
        var data = new Data();
        var workflowId = Guid.NewGuid();
        var inWorkflow = Job("completed");
        inWorkflow.Name = "Pet lovers tees";
        inWorkflow.Config = $$"""{"workflowId":"{{workflowId}}","variationCount":2}""";
        var standalone = Job("completed");
        standalone.Name = "Mugs";
        data.Jobs.AddRange([inWorkflow, standalone]);
        data.JobProducts.AddRange(Enumerable.Range(0, 3).Select(_ => JobProduct(inWorkflow, "image_review_required", completed: HoursAgo(2))));
        data.JobProducts.Add(JobProduct(standalone, "image_review_required", completed: HoursAgo(2)));

        var item = (await CreateService(data).GetActionItemsAsync()).Value.Items.Single();

        item.ActionUrl.Should().Be("/batches");
        item.Targets.Should().Equal(
            new ActionTargetDto("Pet lovers tees", 3, $"/workflows?id={workflowId}&job={inWorkflow.Id}"),
            new ActionTargetDto("Mugs", 1, $"/batch-jobs/{standalone.Id}"));
    }

    [TestMethod]
    public async Task GetActionItemsAsync_OneVideoToReview_OpensItsVideoRun()
    {
        var data = new Data();
        var product = Product("approved");
        var workflow = new Workflow { Id = Guid.NewGuid(), UserId = Seller, Name = "Hoodie launch" };
        var run = new WorkflowRun { Id = Guid.NewGuid(), WorkflowId = workflow.Id, Workflow = workflow };
        data.Products.Add(product);
        data.Workflows.Add(workflow);
        data.Videos.Add(new PromoVideo
        {
            ProductId = product.Id, Product = product, Status = "completed", ApprovalStatus = "pending",
            WorkflowRunId = run.Id, WorkflowRun = run, CreatedAt = HoursAgo(3),
        });

        var item = (await CreateService(data).GetActionItemsAsync()).Value.Items.Single();

        item.Type.Should().Be(ActionItemTypes.VideoReview);
        item.ActionUrl.Should().Be($"/workflows?id={workflow.Id}&run={run.Id}");
        item.Targets.Single().Label.Should().Be("Hoodie launch");
    }

    [TestMethod]
    public async Task GetActionItemsAsync_ListingsToApprove_OpenTheirBatchFilteredToCompleted()
    {
        var data = new Data();
        var batch = new Batch { Id = Guid.NewGuid(), UserId = Seller, Name = "Back catalogue" };
        var product = Product("approved");
        product.BatchId = batch.Id;
        data.Batches.Add(batch);
        data.Listings.Add(new ListingContentEntity { Product = product, ProductId = product.Id, ApprovalStatus = "pending", CreatedAt = HoursAgo(5) });

        var item = (await CreateService(data).GetActionItemsAsync()).Value.Items.Single();

        item.ActionUrl.Should().Be($"/batches?batch={batch.Id}&status=completed");
        item.Targets.Single().Should().Be(new ActionTargetDto("Back catalogue", 1, item.ActionUrl));
    }

    [TestMethod]
    public async Task GetActionItemsAsync_ListingAndSeoOfAProductRunInAWorkflow_OpenItsLatestRun()
    {
        var data = new Data();
        var workflow = new Workflow { Id = Guid.NewGuid(), UserId = Seller, Name = "Etsy POD pipeline" };
        var product = Product("approved");
        var olderRun = new WorkflowRun { Id = Guid.NewGuid(), WorkflowId = workflow.Id, ProductId = product.Id, CreatedAt = HoursAgo(10) };
        var latestRun = new WorkflowRun { Id = Guid.NewGuid(), WorkflowId = workflow.Id, ProductId = product.Id, CreatedAt = HoursAgo(2) };
        var listing = new ListingContentEntity { Product = product, ProductId = product.Id, ApprovalStatus = "pending", CreatedAt = HoursAgo(1) };
        data.Products.Add(product);
        data.Workflows.Add(workflow);
        data.Runs.AddRange([olderRun, latestRun]);
        data.Listings.Add(listing);
        data.SeoScores.Add(new SeoScore { ListingContent = listing, OverallSeoScore = 41, CalculatedAt = HoursAgo(1) });

        var items = (await CreateService(data).GetActionItemsAsync()).Value.Items;

        var runUrl = $"/workflows?id={workflow.Id}&run={latestRun.Id}";
        items.Single(item => item.Type == ActionItemTypes.ListingReview).ActionUrl.Should().Be(runUrl);
        items.Single(item => item.Type == ActionItemTypes.LowSeo).Targets.Single()
            .Should().Be(new ActionTargetDto("Etsy POD pipeline", 1, runUrl));
    }

    [TestMethod]
    public async Task GetSummaryAsync_BuildsADailyTrendTheAiUsageAndTheLatestJobs()
    {
        var data = new Data();
        var job = Job("completed", hoursAgo: 30);
        data.Jobs.Add(job);
        data.Products.AddRange([Product("approved", createdHoursAgo: 1), Product("approved", createdHoursAgo: 25)]);
        data.JobProducts.Add(JobProduct(job, "failed", completed: HoursAgo(1)));
        data.Calls.AddRange([
            new ApiUsageRecord { UserId = Seller, Feature = "image_generation", Status = "success", CostUsd = 0.05m, CreatedAt = HoursAgo(1) },
            new ApiUsageRecord { UserId = Seller, Feature = "image_generation", Status = "failed", CostUsd = 0.01m, CreatedAt = HoursAgo(2) },
            new ApiUsageRecord { UserId = Seller, Feature = "listing", Status = "success", CostUsd = 0.10m, CreatedAt = HoursAgo(2) },
        ]);

        var summary = (await CreateService(data).GetSummaryAsync(7)).Value;

        summary.Trend.Should().HaveCount(7);
        summary.Trend[^1].Should().Be(new TrendPointDto(DateOnly.FromDateTime(Now.UtcDateTime), 1, 1, 1, 0.16m));
        summary.Trend[^2].Products.Should().Be(1);
        summary.AiUsage.Should().Equal(
            new AiFeatureCostDto("listing", 1, 0, 0.10m),
            new AiFeatureCostDto("image_generation", 2, 1, 0.06m));
        summary.RecentJobs.Single().Url.Should().Be($"/batch-jobs/{job.Id}");
    }

    [TestMethod]
    public void DashboardInsights_PointsOutAFailingNicheTheUsualCauseAndAReliableNiche()
    {
        var kpis = new DashboardKpisDto(
            new KpiDto(51, 21, 142.9m), new KpiDto(90, 92, -2.2m), new KpiDto(0, 0, null), new KpiDto(0.07m, 0.069m, 1.5m));
        var niches = new List<NicheOutcome> { new("Christmas", 6, 6), new("Cat lovers", 14, 2), new("Coffee", 2, 0) };

        var insights = DashboardInsights.Build(kpis, niches, ("The image provider timed out", 8, 13));

        insights.Select(insight => insight.Title).Should().Equal(
            "Most failures: \"The image provider timed out\"",
            "Christmas fails often (100%)",
            "Cat lovers is your most reliable niche");
    }

    [TestMethod]
    public void DashboardInsights_QuietPeriod_SaysNothing()
    {
        var flat = new KpiDto(10, 10, 0);

        DashboardInsights.Build(new DashboardKpisDto(flat, flat, flat, flat), [new("Coffee", 2, 1)], null)
            .Should().BeEmpty();
    }

    [TestMethod]
    public void DashboardLinks_ReadTheWorkflowFromTheJobConfigOnlyWhenItIsThere()
    {
        var jobId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();

        DashboardLinks.BatchJob(jobId, $$"""{"workflowId":"{{workflowId}}"}""").Should().Be($"/workflows?id={workflowId}&job={jobId}");
        DashboardLinks.BatchJob(jobId, "{}").Should().Be($"/batch-jobs/{jobId}");
        DashboardLinks.BatchJob(jobId, "not json").Should().Be($"/batch-jobs/{jobId}");
        DashboardLinks.BatchJob(jobId, null).Should().Be($"/batch-jobs/{jobId}");
    }
}
