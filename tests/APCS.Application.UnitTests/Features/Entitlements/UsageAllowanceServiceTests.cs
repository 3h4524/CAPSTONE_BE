using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.UsageStatistics;
using APCS.Application.UnitTests.TestSupport;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Entitlements;

[TestClass]
public sealed class UsageAllowanceServiceTests
{
    private static readonly Guid UserId = SubscriptionTestData.UserId;

    // The test clock reads 11 Sep 2026: Free counts 1 Sep up to 1 Oct.
    private static readonly DateTime ThisMonth = new(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LastMonth = new(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);

    private readonly List<WorkflowNodeRun> nodes = [];
    private readonly List<BatchJob> jobs = [];
    private readonly List<ApiUsageRecord> records = [];
    private readonly List<MockupImage> mockups = [];
    private readonly List<PromoVideo> videos = [];
    private readonly Product product = new() { Id = Guid.NewGuid(), UserId = UserId, Name = "Mug", ProductType = "mug", InputDescription = "", ProcessingStatus = "done" };

    private UsageAllowanceService CreateService() => new(
        Repository(nodes),
        Repository(jobs),
        Repository(records),
        Repository(mockups),
        Repository(videos),
        Options.Create(new UsageAllowanceOptions()),
        SubscriptionTestData.CreateTimeProvider());

    private static IRepository<T> Repository<T>(List<T> items) where T : class
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(candidate => candidate.Query()).Returns(() => items.AsQueryable().BuildMock());
        return repository.Object;
    }

    private static PlanEntitlements Trial(DateTime endsAt) =>
        new(true, Guid.NewGuid(), "Pro", "pro", true, endsAt, 3, 1, false, new Dictionary<string, PlanFeatureValue>());

    private void AddFullRun(DateTime at, string videoNode = "generate-video") =>
        nodes.Add(new WorkflowNodeRun
        {
            Id = Guid.NewGuid(), NodeId = "n", NodeType = videoNode, Status = "succeeded", InputSnapshot = "{}", OutputSnapshot = "{}",
            WorkflowRun = new WorkflowRun { Id = Guid.NewGuid(), UserId = UserId, CreatedAt = at, DefinitionSnapshot = "{}", Status = "completed", IdempotencyKey = "k", RequestHash = "h" }
        });

    private void AddImage(DateTime at, Guid? batchJobId, Guid? productId = null) =>
        records.Add(new ApiUsageRecord
        {
            Id = Guid.NewGuid(), UserId = UserId, Provider = "gemini", Feature = UsageFeatures.ImageGeneration, Status = "success",
            BatchJobId = batchJobId, ProductId = productId ?? product.Id, CreatedAt = at
        });

    [TestMethod]
    public async Task RequireAsync_OnFree_CountsOnlyThisMonthsRunsWithAVideo()
    {
        AddFullRun(ThisMonth);
        AddFullRun(LastMonth);
        AddFullRun(ThisMonth, videoNode: "generate-listing");
        var service = CreateService();

        (await service.RequireAsync(UserId, PlanEntitlements.Free, UsageAllowances.FullRuns)).IsSuccess
            .Should().BeTrue("one of the two runs this month is used; last month's and a listing-only run do not count");

        AddFullRun(ThisMonth);
        var result = await service.RequireAsync(UserId, PlanEntitlements.Free, UsageAllowances.FullRuns);

        result.Error.Code.Should().Be("Plan.AllowanceReached");
        result.Error.Message.Should().Be("Free includes 2 full runs a month and 0 are left. They reset on 1 Oct 2026, or upgrade to keep going.");
    }

    [TestMethod]
    public async Task RequireAsync_DesignImages_CountsDesignJobsOnlyAndTheWholeRequest()
    {
        var jobId = Guid.NewGuid();
        for (var i = 0; i < 5; i++) AddImage(ThisMonth, jobId);
        // An AI studio frame has no design job, so it is not a design image.
        AddImage(ThisMonth, batchJobId: null);
        var service = CreateService();

        (await service.RequireAsync(UserId, PlanEntitlements.Free, UsageAllowances.DesignImages, requested: 2)).IsSuccess.Should().BeTrue("5 + 2 = 7");
        (await service.RequireAsync(UserId, PlanEntitlements.Free, UsageAllowances.DesignImages, requested: 3)).IsFailure.Should().BeTrue("5 + 3 > 7");
    }

    [TestMethod]
    public async Task RequireAsync_OnAPaidPlan_AlwaysPasses()
    {
        for (var i = 0; i < 50; i++) AddFullRun(ThisMonth);
        var paid = new PlanEntitlements(true, Guid.NewGuid(), "Pro", "pro", false, null, -1, -1, false, new Dictionary<string, PlanFeatureValue>());

        (await CreateService().RequireAsync(UserId, paid, UsageAllowances.FullRuns)).IsSuccess.Should().BeTrue();
    }

    [TestMethod]
    public async Task RequireAsync_DuringATrial_CountsTheWholeTrialAndDistinctProducts()
    {
        // The trial ends on 20 Sep, so it began on 6 Sep: an image on 5 Sep is before it.
        var endsAt = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var jobId = Guid.NewGuid();
        var products = Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToList();
        foreach (var productId in products) AddImage(new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc), jobId, productId);
        AddImage(new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc), jobId, products[0]);
        AddImage(new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc), jobId);
        var service = CreateService();

        (await service.RequireAsync(UserId, Trial(endsAt), UsageAllowances.Products)).IsSuccess.Should().BeTrue("9 of 10 products are used");
        var result = await service.RequireAsync(UserId, Trial(endsAt), UsageAllowances.Products, requested: 2);
        result.Error.Message.Should().Be("Your Pro trial includes 10 products and 1 is left. Choose a plan to keep going.");
        (await service.RequireAsync(UserId, Trial(endsAt), UsageAllowances.FullRuns)).IsSuccess.Should().BeTrue("the trial does not count full runs");
    }

    [TestMethod]
    public async Task GetAsync_OnFree_ListsEveryAllowanceWithWhatIsUsedAndWhenItResets()
    {
        AddFullRun(ThisMonth);
        mockups.Add(new MockupImage
        {
            Id = Guid.NewGuid(), ProductId = product.Id, Product = product, SourceType = MockupSources.Generated,
            StorageProvider = "cloudinary", StorageKey = "k", ApprovalStatus = "approved", Role = "Hero", Regions = "{}", CreatedAt = ThisMonth
        });
        videos.Add(new PromoVideo
        {
            Id = Guid.NewGuid(), ProductId = product.Id, Product = product, Mode = "standard", VideoResolution = "1080p", FileFormat = "mp4",
            AspectRatio = "1:1", PlatformTarget = "etsy", Status = "completed", ApprovalStatus = "approved", CreatedAt = ThisMonth
        });
        jobs.Add(new BatchJob
        {
            Id = Guid.NewGuid(), UserId = UserId, Name = "b", SourceFileType = "manual", Status = "completed", Config = "{}", Priority = "normal",
            JobType = "design_generation", StartedAt = ThisMonth
        });

        var summary = await CreateService().GetAsync(UserId, PlanEntitlements.Free);

        summary.Scope.Should().Be("free");
        summary.ResetsOn.Should().Be(new DateOnly(2026, 10, 1));
        summary.MockupsPerRun.Should().Be(3);
        summary.Items.Select(item => (item.Code, item.Used, item.Limit)).Should().Equal(
            (UsageAllowances.FullRuns, 1, 2),
            (UsageAllowances.DesignRuns, 1, 5),
            (UsageAllowances.DesignImages, 0, 7),
            (UsageAllowances.Mockups, 1, 6),
            (UsageAllowances.Videos, 1, 2),
            (UsageAllowances.Listings, 0, 5));
    }
}
