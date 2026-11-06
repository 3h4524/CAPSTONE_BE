using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;
using APCS.Application.Features.Subscriptions;
using APCS.Application.Features.UsageStatistics;
using APCS.Application.Features.Workflows;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MockQueryable.Moq;
using Moq;
using ListingContentEntity = APCS.Domain.Entities.ListingContent;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class PlanInsightsServiceTests
{
    private const decimal UsdToVnd = 25000m;
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly Error Unavailable = new("Revenue.Unavailable", "Unavailable", ErrorType.Failure);

    private sealed class Data
    {
        public List<Subscription> Subscriptions { get; } = [];
        public List<Product> Products { get; } = [];
        public List<DesignImage> DesignImages { get; } = [];
        public List<ApiUsageRecord> Calls { get; } = [];
    }

    private static PlanInsightsService CreateService(Data data)
    {
        var revenue = new Mock<IRevenueService>();
        revenue.Setup(x => x.GetChartsAsync(It.IsAny<RevenueFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<RevenueChartDto>(Unavailable));
        revenue.Setup(x => x.GetPlanCohortsAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<PlanCohortDto>>(Unavailable));

        return new PlanInsightsService(
            Repo(data.Subscriptions),
            Repo<Invoice>([]),
            Repo(data.Products),
            Repo(data.DesignImages),
            Repo<MockupImage>([]),
            Repo<PromoVideo>([]),
            Repo(data.Calls),
            Repo<ExportPackage>([]),
            Repo<WorkflowRun>([]),
            Repo<BatchJob>([]),
            Repo<MockupTemplate>([]),
            Repo<ListingGenerationHistory>([]),
            Repo<AiPrompt>([]),
            Repo<ListingContentEntity>([]),
            Repo<EtsyUploadLog>([]),
            Repo<PrintifyUploadLog>([]),
            new WorkflowCapabilityRegistry(),
            new FakeTimeProvider(Now),
            Options.Create(new PaymentGatewaySettings(UsdToVnd, null)),
            revenue.Object,
            Options.Create(new PlanVerdictOptions()));
    }

    private static IRepository<T> Repo<T>(List<T> rows) where T : class
    {
        var repo = new Mock<IRepository<T>>();
        repo.Setup(x => x.Query()).Returns(() => rows.AsQueryable().BuildMock());
        return repo.Object;
    }

    private static SubscriptionPlan Plan(string tier, string name, decimal price, params PlanFeature[] features)
    {
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Tier = tier, Name = name, MonthlyPriceUsd = price, IsActive = true };
        foreach (var feature in features) plan.PlanFeatures.Add(feature);
        return plan;
    }

    private static Subscription Subscribe(
        SubscriptionPlan plan,
        Guid? userId = null,
        string status = "active",
        DateTime? trialEndsAt = null,
        DateTime? deletedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId ?? Guid.NewGuid(),
        PlanId = plan.Id,
        Plan = plan,
        Status = status,
        TrialEndsAt = trialEndsAt,
        DeletedAt = deletedAt,
        CreatedAt = Now.UtcDateTime.AddDays(-90),
    };

    private static DesignImage Image(Guid userId, int daysAgo = 1) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = Now.UtcDateTime.AddDays(-daysAgo),
        Product = new Product { Id = Guid.NewGuid(), UserId = userId },
    };

    private static ApiUsageRecord Listing(Guid userId, decimal costUsd, int daysAgo = 1) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Feature = UsageFeatures.Listing,
        CostUsd = costUsd,
        CreatedAt = Now.UtcDateTime.AddDays(-daysAgo),
    };

    private static async Task<PlanInsightsDto> GetAsync(Data data)
    {
        var result = await CreateService(data).GetAsync();

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [TestMethod]
    public async Task GetAsync_WithNoCustomers_ReturnsAnEmptyPeriod()
    {
        var insights = await GetAsync(new Data());

        insights.Days.Should().Be(PlanInsightsService.PeriodDays);
        insights.GeneratedAt.Should().Be(Now.UtcDateTime);
        insights.Plans.Should().BeEmpty();
        insights.Verdicts.Should().BeEmpty();
        insights.ChurnSignals.Should().BeEmpty();
        insights.Trial.Started.Should().Be(0);
    }

    [TestMethod]
    public async Task GetAsync_PricesUnitCostsInVnd()
    {
        var insights = await GetAsync(new Data());

        insights.UnitCosts.Single(x => x.Key == "mockup_render").CostVnd.Should().Be(50m);
        insights.UnitCosts.Single(x => x.Key == "video_render").CostVnd.Should().Be(250m);
    }

    [TestMethod]
    public async Task GetAsync_CountsOnlySubscriptionsInEffect()
    {
        var data = new Data();
        var pro = Plan("pro", "Pro", 29);
        data.Subscriptions.Add(Subscribe(pro));
        data.Subscriptions.Add(Subscribe(pro, status: "cancelled"));
        data.Subscriptions.Add(Subscribe(pro, deletedAt: Now.UtcDateTime));
        data.Subscriptions.Add(Subscribe(pro, trialEndsAt: Now.UtcDateTime.AddDays(-1)));

        var insights = await GetAsync(data);

        insights.Plans.Should().ContainSingle().Which.Subscribers.Should().Be(1);
    }

    [TestMethod]
    public async Task GetAsync_KeepsRunningTrialsApartFromThePaidPlan()
    {
        var data = new Data();
        var pro = Plan("pro", "Pro", 29);
        data.Subscriptions.Add(Subscribe(pro));
        data.Subscriptions.Add(Subscribe(pro, trialEndsAt: Now.UtcDateTime.AddDays(3)));

        var insights = await GetAsync(data);

        insights.Plans.Select(x => (x.Key, x.Name, x.IsTrial, x.MonthlyPriceVnd)).Should().Equal(
            ("pro", "Pro", false, 725000m),
            ("trial", "Pro trial", true, 0m));
    }

    [TestMethod]
    public async Task GetAsync_MeasuresAdoptionWithinThePeriodOnly()
    {
        var data = new Data();
        var pro = Plan("pro", "Pro", 29);
        var active = Subscribe(pro);
        var idle = Subscribe(pro);
        data.Subscriptions.AddRange([active, idle]);
        data.DesignImages.AddRange([Image(active.UserId), Image(active.UserId, 2), Image(idle.UserId, PlanInsightsService.PeriodDays + 5)]);

        var insights = await GetAsync(data);

        var adoption = insights.Plans.Single().Adoption.Single(x => x.Metric == "design_images");
        adoption.Users.Should().Be(1);
        adoption.Rate.Should().Be(50m);
        adoption.MedianUses.Should().Be(2m);
    }

    [TestMethod]
    public async Task GetAsync_MarksAFeatureThePlanDoesNotInclude()
    {
        var data = new Data();
        data.Subscriptions.Add(Subscribe(Plan("starter", "Starter", 9, new PlanFeature { FeatureCode = PlanFeatures.AiVideo, IsEnabled = false })));
        data.Subscriptions.Add(Subscribe(Plan("pro", "Pro", 29, new PlanFeature { FeatureCode = PlanFeatures.AiVideo, IsEnabled = true })));

        var insights = await GetAsync(data);

        insights.Plans.Select(plan => plan.Adoption.Single(x => x.Metric == "ai_videos").IncludedInPlan).Should().Equal(false, true);
    }

    [TestMethod]
    public async Task GetAsync_ScalesPlatformCostToOneMonthPerSubscriber()
    {
        var data = new Data();
        var pro = Plan("pro", "Pro", 30);
        var subscriber = Subscribe(pro);
        data.Subscriptions.Add(subscriber);
        // 4.5 USD over 45 days is 3 USD a month, 10% of the 30 USD price.
        data.Calls.Add(Listing(subscriber.UserId, 4.5m));

        var cost = (await GetAsync(data)).Plans.Single().Cost;

        cost.PlatformCostPerSubscriberVnd.Should().Be(75000m);
        cost.CostShare.Should().Be(10m);
    }

    [TestMethod]
    public async Task GetAsync_CountsSubscribersWhoReachedADailyCap()
    {
        var data = new Data();
        var pro = Plan("pro", "Pro", 29, new PlanFeature { FeatureCode = PlanFeatures.DailyListingCap, IsEnabled = true, LimitValue = 2 });
        var capped = Subscribe(pro);
        var under = Subscribe(pro);
        data.Subscriptions.AddRange([capped, under]);
        data.Calls.AddRange([Listing(capped.UserId, 0), Listing(capped.UserId, 0), Listing(under.UserId, 0)]);

        var adoption = (await GetAsync(data)).Plans.Single().Adoption.Single(x => x.Metric == "listings");

        adoption.DailyCap.Should().Be(2);
        adoption.UsersAtDailyCap.Should().Be(1);
    }

    [TestMethod]
    public async Task UsersOfFeatureAsync_WhenTheFeatureHasNoMetric_ReturnsNull()
    {
        var users = await CreateService(new Data()).UsersOfFeatureAsync("unknown", Now.UtcDateTime.AddDays(-1), Now.UtcDateTime);

        users.Should().BeNull();
    }

    [TestMethod]
    public async Task UsersOfFeatureAsync_ForListingContent_CountsGeneratedListings()
    {
        var data = new Data();
        var seller = Guid.NewGuid();
        data.Calls.AddRange([Listing(seller, 0), Listing(seller, 0), Listing(Guid.NewGuid(), 0, daysAgo: 30)]);

        var users = await CreateService(data).UsersOfFeatureAsync(
            PlanFeatures.WorkflowListing, Now.UtcDateTime.AddDays(-7), Now.UtcDateTime);

        users.Should().BeEquivalentTo([seller]);
    }
}
