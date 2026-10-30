using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Entitlements;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Entitlements;

[TestClass]
public sealed class PlanEntitlementServiceTests
{
    private static readonly Guid UserId = SubscriptionTestData.UserId;

    private readonly Mock<IUsageAllowanceService> allowances = new();
    private readonly List<ApiKey> keys = [];

    private static SubscriptionPlan CreatePlan(params PlanFeature[] features)
    {
        var plan = SubscriptionTestData.CreateStarterPlan();
        foreach (var feature in features)
        {
            feature.PlanId = plan.Id;
            plan.PlanFeatures.Add(feature);
        }

        return plan;
    }

    private static Subscription Trial(SubscriptionPlan plan)
    {
        var trial = SubscriptionTestData.CreateActiveSubscription(plan);
        trial.TrialEndsAt = SubscriptionTestData.UtcNow.UtcDateTime.AddDays(5);
        return trial;
    }

    private PlanEntitlementService CreateService(Subscription? subscription, int mockupsRenderedToday = 0)
    {
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        var product = new Product { Id = Guid.NewGuid(), UserId = UserId, Name = "Mug", ProductType = "mug", InputDescription = "", ProcessingStatus = "done" };
        var renderedAt = SubscriptionTestData.UtcNow.UtcDateTime.AddHours(-1);
        var mockups = Enumerable.Range(0, mockupsRenderedToday)
            .Select(_ => new MockupImage
            {
                Id = Guid.NewGuid(), ProductId = product.Id, Product = product, SourceType = MockupSources.Generated,
                StorageProvider = "cloudinary", StorageKey = "k", ApprovalStatus = "pending", Role = "Hero", Regions = "{}",
                CreatedAt = renderedAt
            })
            .ToList();
        var mockupRepository = new Mock<IRepository<MockupImage>>();
        mockupRepository.Setup(candidate => candidate.Query()).Returns(mockups.AsQueryable().BuildMock());

        var apiKeys = new Mock<IApiKeyRepository>();
        apiKeys.Setup(candidate => candidate.ListOwnedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() => keys);

        return new PlanEntitlementService(
            subscriptions.Object,
            mockupRepository.Object,
            Mock.Of<IRepository<PromoVideo>>(),
            Mock.Of<IRepository<ApiUsageRecord>>(),
            apiKeys.Object,
            allowances.Object,
            Options.Create(new UsageAllowanceOptions()),
            SubscriptionTestData.CreateTimeProvider());
    }

    [TestMethod]
    public async Task GetAsync_WithoutASubscription_IsFreeWithOneProductOneVariationAndListings()
    {
        var service = CreateService(subscription: null);

        var entitlements = await service.GetAsync(UserId);

        entitlements.HasPlan.Should().BeFalse();
        entitlements.IsFree.Should().BeTrue();
        entitlements.MaxBatchSize.Should().Be(1);
        entitlements.MaxMockupsPerRun.Should().Be(3);
        entitlements.Limit(PlanFeatures.DesignVariations).Should().Be(1);
        entitlements.IsEnabled(PlanFeatures.WorkflowListing).Should().BeTrue();
        entitlements.IsEnabled(PlanFeatures.AllVideoFormats).Should().BeFalse("Free makes standard videos only");
        entitlements.IsEnabled(PlanFeatures.AiVideo).Should().BeFalse();
    }

    [TestMethod]
    public async Task GetAsync_DuringATrial_LeavesOutTheAiVideoToolsAndRunsThreeProducts()
    {
        var plan = CreatePlan(
            new PlanFeature { FeatureCode = PlanFeatures.AiVideo, IsEnabled = true },
            new PlanFeature { FeatureCode = PlanFeatures.StudioKeyframe, IsEnabled = true },
            new PlanFeature { FeatureCode = PlanFeatures.AllVideoFormats, IsEnabled = true });
        plan.MaxBatchSize = 200;
        var service = CreateService(Trial(plan));

        var entitlements = await service.GetAsync(UserId);

        entitlements.IsTrial.Should().BeTrue();
        entitlements.IsEnabled(PlanFeatures.AiVideo).Should().BeFalse();
        entitlements.IsEnabled(PlanFeatures.StudioKeyframe).Should().BeFalse();
        entitlements.IsEnabled(PlanFeatures.AllVideoFormats).Should().BeTrue("the trial keeps landscape standard videos");
        entitlements.MaxBatchSize.Should().Be(3);
        entitlements.MaxConcurrentJobs.Should().Be(1);
    }

    [TestMethod]
    public async Task RequireFeatureAsync_ForAFeatureThePlanSwitchesOff_ReturnsFeatureLocked()
    {
        var plan = CreatePlan(new PlanFeature { FeatureCode = PlanFeatures.AiVideo, IsEnabled = false });
        var service = CreateService(SubscriptionTestData.CreateActiveSubscription(plan));

        var result = await service.RequireFeatureAsync(UserId, PlanFeatures.AiVideo);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Plan.FeatureLocked");
    }

    [TestMethod]
    public async Task RequireFeatureAsync_ForAFeatureWithNoRow_ReturnsFeatureLocked()
    {
        var service = CreateService(SubscriptionTestData.CreateActiveSubscription(CreatePlan()));

        var result = await service.RequireFeatureAsync(UserId, PlanFeatures.LifestyleMockup);

        result.IsFailure.Should().BeTrue();
    }

    [TestMethod]
    public async Task RequireFeatureAsync_ForAFeatureThePlanIncludes_Succeeds()
    {
        var plan = CreatePlan(new PlanFeature { FeatureCode = PlanFeatures.LifestyleMockup, IsEnabled = true });
        var service = CreateService(SubscriptionTestData.CreateActiveSubscription(plan));

        var result = await service.RequireFeatureAsync(UserId, PlanFeatures.LifestyleMockup);

        result.IsSuccess.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetAsync_ALimitWithNoRow_MeansNoLimit()
    {
        var service = CreateService(SubscriptionTestData.CreateActiveSubscription(CreatePlan()));

        var entitlements = await service.GetAsync(UserId);

        entitlements.Limit(PlanFeatures.DesignVariations).Should().BeNull();
    }

    [TestMethod]
    public async Task RequireDailyCapacityAsync_BelowTheCap_Succeeds()
    {
        var plan = CreatePlan(new PlanFeature { FeatureCode = PlanFeatures.DailyMockupCap, IsEnabled = true, LimitValue = 3 });
        var service = CreateService(SubscriptionTestData.CreateActiveSubscription(plan), mockupsRenderedToday: 2);

        var result = await service.RequireDailyCapacityAsync(UserId, PlanFeatures.DailyMockupCap);

        result.IsSuccess.Should().BeTrue();
    }

    [TestMethod]
    public async Task RequireDailyCapacityAsync_AtTheCap_ReturnsDailyCapReached()
    {
        var plan = CreatePlan(new PlanFeature { FeatureCode = PlanFeatures.DailyMockupCap, IsEnabled = true, LimitValue = 3 });
        var service = CreateService(SubscriptionTestData.CreateActiveSubscription(plan), mockupsRenderedToday: 3);

        var result = await service.RequireDailyCapacityAsync(UserId, PlanFeatures.DailyMockupCap);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Plan.DailyCapReached");
    }

    [TestMethod]
    public async Task RequireDailyCapacityAsync_OnFreeOrDuringATrial_LeavesItToTheAllowances()
    {
        var plan = CreatePlan(new PlanFeature { FeatureCode = PlanFeatures.DailyMockupCap, IsEnabled = true, LimitValue = 4 });

        (await CreateService(Trial(plan), mockupsRenderedToday: 10).RequireDailyCapacityAsync(UserId, PlanFeatures.DailyMockupCap))
            .IsSuccess.Should().BeTrue();
        (await CreateService(null, mockupsRenderedToday: 10).RequireDailyCapacityAsync(UserId, PlanFeatures.DailyMockupCap))
            .IsSuccess.Should().BeTrue();
    }

    [TestMethod]
    public async Task RequireDailyCapacityAsync_WithNoCapSet_NeverCounts()
    {
        var service = CreateService(SubscriptionTestData.CreateActiveSubscription(CreatePlan()), mockupsRenderedToday: 10_000);

        var result = await service.RequireDailyCapacityAsync(UserId, PlanFeatures.DailyMockupCap);

        result.IsSuccess.Should().BeTrue();
    }

    [TestMethod]
    public async Task RequireAllowanceAsync_AsksTheAllowancesWithTheSellersEntitlements()
    {
        allowances.Setup(candidate => candidate.RequireAsync(UserId, It.Is<PlanEntitlements>(item => item.IsFree), UsageAllowances.Videos, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(PlanErrors.AllowanceReached("videos", 2, 0, UsageAllowances.ScopeFree, new DateOnly(2026, 12, 1))));
        var service = CreateService(subscription: null);

        var result = await service.RequireAllowanceAsync(UserId, UsageAllowances.Videos);

        result.Error.Code.Should().Be("Plan.AllowanceReached");
        result.Error.Message.Should().Be("Free includes 2 videos a month and 0 are left. They reset on 1 Dec 2026, or upgrade to keep going.");
    }

    [TestMethod]
    public async Task RequireGeminiKeyAsync_NeedsAnActiveKeyThatPassedItsCheck()
    {
        var service = CreateService(subscription: null);
        (await service.RequireGeminiKeyAsync(UserId)).Error.Code.Should().Be("Plan.GeminiKeyRequired");

        keys.Add(Key(isActive: true, checkSucceeded: false));
        (await service.RequireGeminiKeyAsync(UserId)).IsFailure.Should().BeTrue("a key that failed its check does not count");

        keys.Add(Key(isActive: true, checkSucceeded: true));
        (await service.RequireGeminiKeyAsync(UserId)).IsSuccess.Should().BeTrue();
    }

    private static ApiKey Key(bool isActive, bool checkSucceeded) => new()
    {
        Id = Guid.NewGuid(), UserId = UserId, ServiceProvider = "gemini", KeyIdentifier = "key", KeyValueEncrypted = "x",
        AuthType = "api_key", IsActive = isActive, LastCheckSucceeded = checkSucceeded
    };
}
