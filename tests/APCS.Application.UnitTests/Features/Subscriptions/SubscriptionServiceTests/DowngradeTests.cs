using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
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
public sealed class DowngradeTests
{
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

    [TestMethod]
    public async Task DowngradeAsync_WhenValidationFails_ReturnsFailure()
    {
        var validator = new Mock<IValidator<DowngradeRequestDto>>();
        validator.Setup(candidate => candidate.ValidateAsync(It.IsAny<DowngradeRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("PlanId", "Required")]));
        var service = SubscriptionTestData.CreateService(downgradeValidator: validator);

        var result = await service.DowngradeAsync(new DowngradeRequestDto(Guid.Empty), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [TestMethod]
    public async Task DowngradeAsync_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.IsAuthenticated).Returns(false);
        var service = SubscriptionTestData.CreateService(currentUser: currentUser);

        var result = await service.DowngradeAsync(new DowngradeRequestDto(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task DowngradeAsync_WhenSellerHasNoActivePaidPlan_ReturnsNoActivePlanToChange()
    {
        var (subscriptions, plans) = CreateRepositories(null, SubscriptionTestData.CreateStarterPlan());
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.DowngradeAsync(
            new DowngradeRequestDto(SubscriptionTestData.CreateStarterPlan().Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionNoActivePlan);
    }

    [TestMethod]
    public async Task DowngradeAsync_WhenTargetPlanIsNotFound_ReturnsPlanNotFound()
    {
        var pro = SubscriptionTestData.CreateProPlan();
        var current = SubscriptionTestData.CreateActiveSubscription(pro);
        var (subscriptions, plans) = CreateRepositories(current, null);
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.DowngradeAsync(new DowngradeRequestDto(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionPlanNotFound);
    }

    [TestMethod]
    public async Task DowngradeAsync_WhenTargetIsNotLowerTier_ReturnsTargetNotLowerTierError()
    {
        var starter = SubscriptionTestData.CreateStarterPlan();
        var current = SubscriptionTestData.CreateActiveSubscription(starter);
        var pro = SubscriptionTestData.CreateProPlan();
        var (subscriptions, plans) = CreateRepositories(current, pro);
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.DowngradeAsync(new DowngradeRequestDto(pro.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionTargetNotLowerTier);
        result.Error.Message.Should().Be("The selected plan is not a downgrade. Please choose a lower-tier plan.");
    }

    [TestMethod]
    public async Task DowngradeAsync_WithAValidLowerTierTarget_SchedulesItForTheRenewalDateWithoutChangingTheCurrentPlan()
    {
        var pro = SubscriptionTestData.CreateProPlan();
        var starter = SubscriptionTestData.CreateStarterPlan();
        var current = SubscriptionTestData.CreateActiveSubscription(pro);
        var (subscriptions, plans) = CreateRepositories(current, starter);
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.DowngradeAsync(new DowngradeRequestDto(starter.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TargetPlanId.Should().Be(starter.Id);
        result.Value.TargetPlanName.Should().Be(starter.Name);
        result.Value.EffectiveDate.Should().Be(current.RenewalDate);

        subscriptions.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Subscription>(sub =>
                    sub.Id == current.Id
                    // The current plan itself is untouched.
                    && sub.PlanId == pro.Id
                    && sub.Status == "active"
                    && sub.ScheduledPlanId == starter.Id
                    && sub.ScheduledPlanEffectiveDate == current.RenewalDate),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task DowngradeAsync_WhenADowngradeIsAlreadyScheduled_ReplacesItWithTheNewSelection()
    {
        // Only one pending downgrade at a time; a new selection replaces the old one.
        var pro = SubscriptionTestData.CreateProPlan();
        var starter = SubscriptionTestData.CreateStarterPlan();
        var free = SubscriptionTestData.CreateFreePlan();
        var current = SubscriptionTestData.CreateActiveSubscription(pro);
        current.ScheduledPlanId = starter.Id;
        current.ScheduledPlanEffectiveDate = current.RenewalDate;
        var (subscriptions, plans) = CreateRepositories(current, free);
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.DowngradeAsync(new DowngradeRequestDto(free.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TargetPlanId.Should().Be(free.Id);
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Subscription>(sub => sub.ScheduledPlanId == free.Id),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task DowngradeAsync_WhenTheScheduledDateHasArrivedUnpaid_KeepsTheCurrentPlanAndReschedules()
    {
        // A scheduled plan only takes over when the renewal carrying it is paid, so reaching its
        // date does not switch the plan or add a free cycle; a new pick just replaces the schedule.
        var pro = SubscriptionTestData.CreateProPlan();
        var starter = SubscriptionTestData.CreateStarterPlan();
        var free = SubscriptionTestData.CreateFreePlan();
        var current = SubscriptionTestData.CreateActiveSubscription(pro);
        var today = DateOnly.FromDateTime(SubscriptionTestData.UtcNow.UtcDateTime);
        current.RenewalDate = today; // Past due today.
        current.ScheduledPlanId = starter.Id;
        current.ScheduledPlan = starter;
        current.ScheduledPlanEffectiveDate = today;
        var (subscriptions, plans) = CreateRepositories(current, free);
        var service = SubscriptionTestData.CreateService(subscriptions, plans);

        var result = await service.DowngradeAsync(new DowngradeRequestDto(free.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Subscription>(sub => sub.PlanId == pro.Id
                    && sub.ScheduledPlanId == free.Id
                    && sub.RenewalDate == today),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
