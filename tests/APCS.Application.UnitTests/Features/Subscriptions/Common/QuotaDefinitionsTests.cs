using APCS.Application.Features.Subscriptions.Common;
using APCS.Application.UnitTests.TestSupport;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Subscriptions.Common;

[TestClass]
public sealed class QuotaDefinitionsTests
{
    [TestMethod]
    public void Build_WhenUsageIsNull_TreatsUsageAsZeroForEveryQuota()
    {
        var plan = SubscriptionTestData.CreateStarterPlan();

        var quotas = QuotaDefinitions.Build(plan, null);

        quotas.Should().OnlyContain(quota => quota.Used == 0 && quota.PercentUsed == 0 && !quota.IsWarning);
    }

    [TestMethod]
    public void Build_ReturnsThreeQuotas_ImageGenerationApiCallsAndStorage()
    {
        var plan = SubscriptionTestData.CreateStarterPlan();

        var quotas = QuotaDefinitions.Build(plan, null);

        quotas.Select(quota => quota.QuotaCode).Should().Contain(["image_generation", "api_calls", "storage"]);
    }

    [TestMethod]
    public void Build_WhenUsageExactlyMatchesLimit_Is100PercentAndWarning()
    {
        var plan = SubscriptionTestData.CreateStarterPlan();
        var usage = new UsageStatistic
        {
            Id = Guid.NewGuid(),
            UserId = SubscriptionTestData.UserId,
            BillingPeriodStart = DateOnly.FromDateTime(SubscriptionTestData.UtcNow.UtcDateTime),
            BillingPeriodEnd = DateOnly.FromDateTime(SubscriptionTestData.UtcNow.UtcDateTime).AddMonths(1),
            ImagesGenerated = plan.ImageGenerationQuota
        };

        var quotas = QuotaDefinitions.Build(plan, usage);

        var imageQuota = quotas.Single(quota => quota.QuotaCode == "image_generation");
        imageQuota.PercentUsed.Should().Be(100);
        imageQuota.IsWarning.Should().BeTrue();
    }

    [TestMethod]
    public void Build_WhenUsageExceedsLimit_ClampsPercentUsedAt100()
    {
        var plan = SubscriptionTestData.CreateStarterPlan();
        var usage = new UsageStatistic
        {
            Id = Guid.NewGuid(),
            UserId = SubscriptionTestData.UserId,
            BillingPeriodStart = DateOnly.FromDateTime(SubscriptionTestData.UtcNow.UtcDateTime),
            BillingPeriodEnd = DateOnly.FromDateTime(SubscriptionTestData.UtcNow.UtcDateTime).AddMonths(1),
            ImagesGenerated = plan.ImageGenerationQuota * 5
        };

        var quotas = QuotaDefinitions.Build(plan, usage);

        quotas.Single(quota => quota.QuotaCode == "image_generation").PercentUsed.Should().Be(100);
    }

    [TestMethod]
    public void Build_WhenPlanLimitIsZero_DoesNotDivideByZero()
    {
        var plan = SubscriptionTestData.CreateStarterPlan();
        plan.ApiCallQuota = 0;

        var quotas = QuotaDefinitions.Build(plan, null);

        var apiQuota = quotas.Single(quota => quota.QuotaCode == "api_calls");
        apiQuota.PercentUsed.Should().Be(0);
        apiQuota.IsWarning.Should().BeFalse();
    }

    [TestMethod]
    public void Build_WithPartialUsage_RoundsPercentUsedSensibly()
    {
        var plan = SubscriptionTestData.CreateStarterPlan();
        var usage = new UsageStatistic
        {
            Id = Guid.NewGuid(),
            UserId = SubscriptionTestData.UserId,
            BillingPeriodStart = DateOnly.FromDateTime(SubscriptionTestData.UtcNow.UtcDateTime),
            BillingPeriodEnd = DateOnly.FromDateTime(SubscriptionTestData.UtcNow.UtcDateTime).AddMonths(1),
            ImagesGenerated = plan.ImageGenerationQuota / 2
        };

        var quotas = QuotaDefinitions.Build(plan, usage);

        quotas.Single(quota => quota.QuotaCode == "image_generation").PercentUsed.Should().Be(50);
    }
}
