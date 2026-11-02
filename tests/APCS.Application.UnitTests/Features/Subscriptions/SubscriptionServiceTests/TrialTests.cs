using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Subscriptions.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Subscriptions.SubscriptionServiceTests;

[TestClass]
public sealed class TrialTests
{
    private static SubscriptionPlan CreateTrialPlan()
    {
        var pro = SubscriptionTestData.CreateProPlan();
        pro.Tier = "pro";
        return pro;
    }

    private static Subscription CreateTrial(SubscriptionPlan plan)
    {
        var trial = SubscriptionTestData.CreateActiveSubscription(plan);
        trial.MonthlyPriceUsd = 0m;
        trial.TrialEndsAt = SubscriptionTestData.UtcNow.UtcDateTime.AddDays(10);
        return trial;
    }

    [TestMethod]
    public async Task StartTrialAsync_ForASellerWithoutAPlan_StartsAFreeFourteenDayProTrial()
    {
        var pro = CreateTrialPlan();
        var subscriptions = new Mock<ISubscriptionRepository>();
        Subscription? added = null;
        subscriptions.Setup(candidate => candidate.AddAsync(It.IsAny<Subscription>(), false, It.IsAny<CancellationToken>()))
            .Callback<Subscription, bool, CancellationToken>((subscription, _, _) => added = subscription)
            .Returns(Task.CompletedTask);
        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetPurchasableByTierAsync("pro", It.IsAny<CancellationToken>())).ReturnsAsync(pro);
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = SubscriptionTestData.CreateService(subscriptions, plans, unitOfWork: unitOfWork);

        var result = await service.StartTrialAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TrialEndsAt.Should().Be(SubscriptionTestData.UtcNow.UtcDateTime.AddDays(14));
        added.Should().NotBeNull();
        added!.PlanId.Should().Be(pro.Id);
        added.Status.Should().Be("active");
        added.MonthlyPriceUsd.Should().Be(0m, "a trial is free and must never count as revenue");
        added.AutoRenew.Should().BeFalse();
        unitOfWork.Verify(candidate => candidate.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task StartTrialAsync_GivesThePlanTheAdminChose()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        starter.Tier = "starter";
        var subscriptions = new Mock<ISubscriptionRepository>();
        Subscription? added = null;
        subscriptions.Setup(candidate => candidate.AddAsync(It.IsAny<Subscription>(), false, It.IsAny<CancellationToken>()))
            .Callback<Subscription, bool, CancellationToken>((subscription, _, _) => added = subscription)
            .Returns(Task.CompletedTask);
        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetPurchasableByTierAsync("starter", It.IsAny<CancellationToken>())).ReturnsAsync(starter);
        var service = SubscriptionTestData.CreateService(subscriptions, plans, planSettings: new TestPlanSettings(trialPlanTier: "starter"));

        var result = await service.StartTrialAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.PlanId.Should().Be(starter.Id);
        plans.Verify(candidate => candidate.GetPurchasableByTierAsync("pro", It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StartTrialAsync_WithoutAWorkingGeminiKey_IsRefused()
    {
        var subscriptions = new Mock<ISubscriptionRepository>();
        var entitlements = TestEntitlements.Free();
        entitlements.HasGeminiKey = false;
        var service = SubscriptionTestData.CreateService(subscriptions, entitlements: entitlements);

        var result = await service.StartTrialAsync(CancellationToken.None);

        result.Error.Code.Should().Be("Plan.GeminiKeyRequired");
        subscriptions.Verify(candidate => candidate.AddAsync(It.IsAny<Subscription>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StartTrialAsync_WhenTheAccountAlreadyHadATrial_ReturnsTrialAlreadyUsed()
    {
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.HasUsedTrialAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = SubscriptionTestData.CreateService(subscriptions);

        var result = await service.StartTrialAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionTrialAlreadyUsed);
        subscriptions.Verify(candidate => candidate.AddAsync(It.IsAny<Subscription>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StartTrialAsync_WhenTheSellerHasAPlan_ReturnsTrialNotAvailable()
    {
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionTestData.CreateActiveSubscription(SubscriptionTestData.CreateStarterPlan()));
        var service = SubscriptionTestData.CreateService(subscriptions);

        var result = await service.StartTrialAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionTrialNotAvailableWithPlan);
    }

    [TestMethod]
    public async Task GetOverviewAsync_ForASellerWhoNeverHadATrial_OffersOne()
    {
        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetComparisonPlansAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetRecentWithPlanAsync(SubscriptionTestData.UserId, 3, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = SubscriptionTestData.CreateService(plans: plans, invoices: invoices);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        result.Value.TrialEligible.Should().BeTrue();
        result.Value.TrialDays.Should().Be(14);
    }

    [TestMethod]
    public async Task GetOverviewAsync_DuringATrial_ShowsItAndTreatsTheSellerAsNotPaying()
    {
        var pro = CreateTrialPlan();
        var trial = CreateTrial(pro);
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trial);
        subscriptions.Setup(candidate => candidate.HasUsedTrialAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetComparisonPlansAsync(pro.Id, It.IsAny<CancellationToken>())).ReturnsAsync([pro]);
        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(candidate => candidate.GetRecentWithPlanAsync(SubscriptionTestData.UserId, 3, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = SubscriptionTestData.CreateService(subscriptions, plans, invoices: invoices);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        result.Value.CurrentSubscription!.IsTrial.Should().BeTrue();
        result.Value.CurrentSubscription.TrialEndsAt.Should().Be(trial.TrialEndsAt);
        result.Value.HasActivePaidPlan.Should().BeFalse("a trial Seller buys a plan rather than upgrading");
        result.Value.TrialEligible.Should().BeFalse();
    }

    [TestMethod]
    public async Task InitiateCheckoutAsync_DuringATrial_KeepsTheTrialRunningUntilThePaymentSucceeds()
    {
        var pro = CreateTrialPlan();
        var trial = CreateTrial(pro);
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trial);
        var plans = new Mock<IPlanRepository>();
        plans.Setup(candidate => candidate.GetPurchasableByIdAsync(pro.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pro);
        var transaction = SubscriptionTestData.CreateTransaction();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(candidate => candidate.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
        var service = SubscriptionTestData.CreateService(subscriptions, plans, unitOfWork: unitOfWork);

        var result = await service.InitiateCheckoutAsync(new CheckoutRequestDto(pro.Id, "monthly"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        trial.Status.Should().Be("active");
        subscriptions.Verify(candidate => candidate.UpdateAsync(trial, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
