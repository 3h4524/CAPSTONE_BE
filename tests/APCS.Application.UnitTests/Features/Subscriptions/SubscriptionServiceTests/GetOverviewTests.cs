using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Subscriptions.SubscriptionServiceTests;

[TestClass]
public sealed class GetOverviewTests
{
    [TestMethod]
    public async Task GetOverviewAsync_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.IsAuthenticated).Returns(false);
        var service = SubscriptionTestData.CreateService(currentUser: currentUser);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task GetOverviewAsync_ForBrandNewSeller_ReturnsNoCurrentSubscriptionAndEmptyQuotas()
    {
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subscription?)null);

        var starter = SubscriptionTestData.CreateStarterPlan();
        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetComparisonPlansAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([starter]);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetRecentWithPlanAsync(SubscriptionTestData.UserId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = SubscriptionTestData.CreateService(subscriptions, plans, invoices: invoices);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentSubscription.Should().BeNull();
        result.Value.UsageQuotas.Should().BeEmpty();
        result.Value.HasActivePaidPlan.Should().BeFalse();
        result.Value.AvailablePlans.Should().ContainSingle();
        result.Value.AvailablePlans[0].IsCurrentPlan.Should().BeFalse();
    }

    [TestMethod]
    public async Task GetOverviewAsync_WhenUsageStatisticsAreUninitialized_RendersQuotasAtZero()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var activeSubscription = SubscriptionTestData.CreateActiveSubscription(starter);

        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSubscription);

        var usageStatistics = new Mock<IUsageStatisticRepository>();
        usageStatistics.Setup(candidate => candidate.GetCurrentPeriodAsync(
                SubscriptionTestData.UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsageStatistic?)null);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetComparisonPlansAsync(starter.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([starter]);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetRecentWithPlanAsync(SubscriptionTestData.UserId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = SubscriptionTestData.CreateService(subscriptions, plans, invoices: invoices, usageStatistics: usageStatistics);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UsageQuotas.Should().NotBeEmpty();
        result.Value.UsageQuotas.Should().OnlyContain(quota => quota.Used == 0 && quota.PercentUsed == 0);
    }

    [TestMethod]
    public async Task GetOverviewAsync_WhenAQuotaReaches100Percent_MarksItAsWarning()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var activeSubscription = SubscriptionTestData.CreateActiveSubscription(starter);

        var usage = new UsageStatistic
        {
            Id = Guid.NewGuid(),
            UserId = SubscriptionTestData.UserId,
            BillingPeriodStart = activeSubscription.StartDate,
            BillingPeriodEnd = activeSubscription.RenewalDate,
            ImagesGenerated = starter.ImageGenerationQuota,
            TotalApiCalls = 0,
            StorageUsedGb = 0
        };

        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSubscription);

        var usageStatistics = new Mock<IUsageStatisticRepository>();
        usageStatistics.Setup(candidate => candidate.GetCurrentPeriodAsync(
                SubscriptionTestData.UserId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(usage);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetComparisonPlansAsync(starter.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([starter]);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetRecentWithPlanAsync(SubscriptionTestData.UserId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = SubscriptionTestData.CreateService(subscriptions, plans, invoices: invoices, usageStatistics: usageStatistics);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        var imageQuota = result.Value.UsageQuotas.Single(quota => quota.QuotaCode == "image_generation");
        imageQuota.PercentUsed.Should().Be(100);
        imageQuota.IsWarning.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetOverviewAsync_WhenSellerHasAnActivePaidPlan_MarksItsCardAsCurrentAndSetsHasActivePaidPlan()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var pro = SubscriptionTestData.CreateProPlan();
        var activeSubscription = SubscriptionTestData.CreateActiveSubscription(starter);

        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSubscription);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetComparisonPlansAsync(starter.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([starter, pro]);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetRecentWithPlanAsync(SubscriptionTestData.UserId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = SubscriptionTestData.CreateService(subscriptions, plans, invoices: invoices);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        result.Value.HasActivePaidPlan.Should().BeTrue();
        result.Value.AvailablePlans.Single(plan => plan.PlanId == starter.Id).IsCurrentPlan.Should().BeTrue();
        result.Value.AvailablePlans.Single(plan => plan.PlanId == pro.Id).IsCurrentPlan.Should().BeFalse();
    }

    [TestMethod]
    public async Task GetOverviewAsync_WhenSellerIsOnTheFreePlan_HasActivePaidPlanIsFalse()
    {
        var free = SubscriptionTestData.CreateFreePlan();
        var activeSubscription = SubscriptionTestData.CreateActiveSubscription(free);

        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSubscription);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetComparisonPlansAsync(free.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([free]);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetRecentWithPlanAsync(SubscriptionTestData.UserId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = SubscriptionTestData.CreateService(subscriptions, plans, invoices: invoices);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        result.Value.HasActivePaidPlan.Should().BeFalse();
    }

    [TestMethod]
    public async Task GetOverviewAsync_ReturnsAtMostThreeRecentInvoices()
    {
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subscription?)null);

        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetComparisonPlansAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetRecentWithPlanAsync(SubscriptionTestData.UserId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = SubscriptionTestData.CreateService(subscriptions, plans, invoices: invoices);

        await service.GetOverviewAsync(CancellationToken.None);

        invoices.Verify(
            candidate => candidate.GetRecentWithPlanAsync(SubscriptionTestData.UserId, 3, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
