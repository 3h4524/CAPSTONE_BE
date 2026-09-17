using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.UnitTests.TestSupport;
using APCS.Common.Constants;
using APCS.Domain.Entities;
using FluentAssertions;
using Moq;

namespace APCS.Application.UnitTests.Features.Subscriptions.SubscriptionServiceTests;

[TestClass]
public sealed class CancelScheduledDowngradeTests
{
    [TestMethod]
    public async Task CancelScheduledDowngradeAsync_WhenNotAuthenticated_ReturnsUnauthorized()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(candidate => candidate.IsAuthenticated).Returns(false);
        var service = SubscriptionTestData.CreateService(currentUser: currentUser);

        var result = await service.CancelScheduledDowngradeAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Unauthorized);
    }

    [TestMethod]
    public async Task CancelScheduledDowngradeAsync_WhenNothingIsScheduled_ReturnsNoScheduledDowngrade()
    {
        var pro = SubscriptionTestData.CreateProPlan();
        var current = SubscriptionTestData.CreateActiveSubscription(pro);
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        var service = SubscriptionTestData.CreateService(subscriptions);

        var result = await service.CancelScheduledDowngradeAsync(CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.SubscriptionNoScheduledDowngrade);
    }

    [TestMethod]
    public async Task CancelScheduledDowngradeAsync_WhenOneIsScheduled_ClearsItAndKeepsTheCurrentPlan()
    {
        // Cancelling reverts to "no scheduled change"; the current plan is untouched.
        var pro = SubscriptionTestData.CreateProPlan();
        var starter = SubscriptionTestData.CreateStarterPlan();
        var current = SubscriptionTestData.CreateActiveSubscription(pro);
        current.ScheduledPlanId = starter.Id;
        current.ScheduledPlan = starter;
        current.ScheduledPlanEffectiveDate = current.RenewalDate;
        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions.Setup(candidate => candidate.GetActiveWithPlanAsync(SubscriptionTestData.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        var service = SubscriptionTestData.CreateService(subscriptions);

        var result = await service.CancelScheduledDowngradeAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        subscriptions.Verify(
            candidate => candidate.UpdateAsync(
                It.Is<Subscription>(sub =>
                    sub.Id == current.Id
                    && sub.PlanId == pro.Id
                    && sub.ScheduledPlanId == null
                    && sub.ScheduledPlanEffectiveDate == null),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
