using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Subscriptions.Common;
using APCS.Application.Features.Subscriptions.Dtos.Request;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;

namespace APCS.Application.UnitTests.Features.Subscriptions.SubscriptionServiceTests;

[TestClass]
public sealed class UpgradeTests
{
    /// <summary>
    /// A Starter subscription 10 days into a 30-day monthly cycle as of
    /// <see cref="SubscriptionTestData.UtcNow"/> (2026-09-11): started 2026-09-01, renews
    /// 2026-10-01 — 20 remaining days out of 30, so proration math has clean, unambiguous numbers.
    /// </summary>
    private static Subscription CreatePartWayThroughCycleSubscription(SubscriptionPlan plan)
    {
        var subscription = SubscriptionTestData.CreateActiveSubscription(plan);
        subscription.StartDate = new DateOnly(2026, 9, 1);
        subscription.RenewalDate = new DateOnly(2026, 10, 1);
        return subscription;
    }

    private static (Mock<ISubscriptionRepository> Subscriptions, Mock<IPlanRepository> Plans) CreateRepositories(
        Subscription? current, SubscriptionPlan? target)
    {
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);

        var plans = new Mock<IPlanRepository>();
        if (target is not null)
        {
            plans.Setup(candidate => candidate.GetPurchasableByIdAsync(target.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(target);
        }

        return (subscriptions, plans);
    }

    /// <summary>
    /// A committing transaction, needed by any test that reaches the "payment due" branch
    /// (mirrors CheckoutTests.cs's identical helper for the same PayOS-checkout code shape).
    /// </summary>
    private static Mock<IUnitOfWork> CreateCommittingUnitOfWork()
    {
        var transaction = SubscriptionTestData.CreateTransaction();
        transaction.Setup(candidate => candidate.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(context => context.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction.Object);
        return unitOfWork;
    }

    [TestMethod]
    public async Task UpgradeAsync_WhenValidationFails_ReturnsFailure()
    {
        var validator = new Mock<IValidator<UpgradeRequestDto>>();
        validator.Setup(candidate => candidate.ValidateAsync(It.IsAny<UpgradeRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("PlanId", "Required")]));
        var service = SubscriptionTestData.CreateService(upgradeValidator: validator);

        var result = await service.UpgradeAsync(new UpgradeRequestDto(Guid.Empty), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [TestMethod]
    public async Task UpgradeAsync_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.IsAuthenticated).Returns(false);
        var service = SubscriptionTestData.CreateService(currentUser: currentUser);

        var result = await service.UpgradeAsync(new UpgradeRequestDto(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task UpgradeAsync_WhenSellerHasNoActivePaidPlan_ReturnsNoActivePlanToChange()
    {
        var (subscriptions, plans) = CreateRepositories(null, SubscriptionTestData.CreateProPlan());
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.UpgradeAsync(
            new UpgradeRequestDto(SubscriptionTestData.CreateProPlan().Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionNoActivePlan);
    }

    [TestMethod]
    public async Task UpgradeAsync_WhenTargetPlanIsNotFound_ReturnsPlanNotFound()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var current = CreatePartWayThroughCycleSubscription(starter);
        var (subscriptions, plans) = CreateRepositories(current, null);
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.UpgradeAsync(new UpgradeRequestDto(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionPlanNotFound);
    }

    [TestMethod]
    public async Task UpgradeAsync_WhenTargetIsNotHigherTier_ReturnsTargetNotHigherTierError()
    {
        var pro = SubscriptionTestData.CreateProPlan();
        var current = CreatePartWayThroughCycleSubscription(pro);
        var starter = SubscriptionTestData.CreateStarterPlan();
        var (subscriptions, plans) = CreateRepositories(current, starter);
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.UpgradeAsync(new UpgradeRequestDto(starter.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionTargetNotHigherTier);
        result.Error.Message.Should().Be("The selected plan is not an upgrade. Please choose a higher-tier plan.");
    }

    [TestMethod]
    public async Task UpgradeAsync_WhenCurrentCycleIsAnnualAndTargetHasNoAnnualPrice_ReturnsAnnualNotAvailable()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var current = CreatePartWayThroughCycleSubscription(starter);
        current.BillingCycle = "annual";
        var pro = SubscriptionTestData.CreateProPlan(); // Pro has no AnnualPriceUsd.
        var (subscriptions, plans) = CreateRepositories(current, pro);
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.UpgradeAsync(new UpgradeRequestDto(pro.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionAnnualNotAvailable);
    }

    [TestMethod]
    public async Task UpgradeAsync_WithDaysRemainingInTheCycle_ComputesTheProratedDifferenceOnly()
    {
        // Starter ($9.99/mo) -> Pro ($29.99/mo), 20 of 30 days remaining.
        // proratedNewCharge = 29.99 * 20/30 = 19.99 (rounded); credit = 9.99 * 20/30 = 6.66.
        // Due today is the *difference*, not the new plan's full price: 19.99 - 6.66 = 13.33.
        var starter = SubscriptionTestData.CreateStarterPlan();
        var pro = SubscriptionTestData.CreateProPlan();
        var current = CreatePartWayThroughCycleSubscription(starter);
        var (subscriptions, plans) = CreateRepositories(current, pro);
        var unitOfWork = CreateCommittingUnitOfWork();
        var service = SubscriptionTestData.CreateService(subscriptions, plans, unitOfWork: unitOfWork);

        var result = await service.UpgradeAsync(new UpgradeRequestDto(pro.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PaymentRequired.Should().BeTrue();
        result.Value.ProratedCreditUsd.Should().Be(6.66m);
        result.Value.DueTodayUsd.Should().Be(13.33m);
        result.Value.FirstRenewalDate.Should().Be(current.RenewalDate);
    }

    [TestMethod]
    public async Task UpgradeAsync_WithPaymentDue_CreatesAPendingSubscriptionAndLeavesTheCurrentOneActive()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var pro = SubscriptionTestData.CreateProPlan();
        var current = CreatePartWayThroughCycleSubscription(starter);
        var (subscriptions, plans) = CreateRepositories(current, pro);
        var unitOfWork = CreateCommittingUnitOfWork();
        var service = SubscriptionTestData.CreateService(subscriptions, plans, unitOfWork: unitOfWork);

        var result = await service.UpgradeAsync(new UpgradeRequestDto(pro.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        subscriptions.Verify(
            candidate => candidate.AddAsync(
                It.Is<Subscription>(sub =>
                    sub.PlanId == pro.Id
                    && sub.Status == "trialing"
                    && sub.RenewalDate == current.RenewalDate), // Unchanged renewal date.
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        // The previous plan stays untouched until payment actually succeeds.
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(It.IsAny<Subscription>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task UpgradeAsync_OnTheRenewalDateItself_ActivatesImmediatelyWithNoPaymentDue()
    {
        // remainingDays == 0 on the renewal date itself, so both the prorated charge and the
        // credit are zero — nothing is due today.
        var starter = SubscriptionTestData.CreateStarterPlan();
        var pro = SubscriptionTestData.CreateProPlan();
        var current = SubscriptionTestData.CreateActiveSubscription(starter);
        current.StartDate = new DateOnly(2026, 8, 11);
        current.RenewalDate = new DateOnly(2026, 9, 11); // == SubscriptionTestData.UtcNow's date.
        var (subscriptions, plans) = CreateRepositories(current, pro);
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        var service = SubscriptionTestData.CreateService(subscriptions, plans, paymentGateway: paymentGateway);

        var result = await service.UpgradeAsync(new UpgradeRequestDto(pro.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PaymentRequired.Should().BeFalse();
        result.Value.DueTodayUsd.Should().Be(0m);
        result.Value.ProratedCreditUsd.Should().Be(0m);
        paymentGateway.Verify(
            candidate => candidate.CreatePaymentLinkAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // The Subscription record is updated to the Target Plan and remains Active.
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Subscription>(sub => sub.Id == current.Id && sub.PlanId == pro.Id && sub.Status == "active"),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task UpgradeAsync_WhenTheGatewayThrows_ReturnsGatewayUnavailableAndWritesNothing()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var pro = SubscriptionTestData.CreateProPlan();
        var current = CreatePartWayThroughCycleSubscription(starter);
        var (subscriptions, plans) = CreateRepositories(current, pro);
        var paymentGateway = new Mock<IPaymentGatewayClient>();
        paymentGateway.Setup(candidate => candidate.CreatePaymentLinkAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("PayOS unreachable"));
        var service = SubscriptionTestData.CreateService(subscriptions, plans, paymentGateway: paymentGateway);

        var result = await service.UpgradeAsync(new UpgradeRequestDto(pro.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.PaymentGatewayUnavailable);
        subscriptions.Verify(
            candidate => candidate.AddAsync(It.IsAny<Subscription>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
