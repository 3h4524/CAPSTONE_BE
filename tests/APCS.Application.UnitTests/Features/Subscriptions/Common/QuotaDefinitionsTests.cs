using APCS.Application.Features.Subscriptions.Common;
using APCS.Application.UnitTests.TestSupport;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Subscriptions.Common;

[TestClass]
public sealed class QuotaDefinitionsTests
{
    [TestMethod]
    public void Build_WhenUsageIsNull_ReportsZeroUsed()
    {
        var quotas = QuotaDefinitions.Build(null);

        quotas.Should().OnlyContain(quota => quota.Used == 0 && quota.PercentUsed == 0 && !quota.IsWarning);
    }

    [TestMethod]
    public void Build_ReportsImageGenerationWithoutALimit()
    {
        var quotas = QuotaDefinitions.Build(null);

        quotas.Should().ContainSingle();
        quotas[0].QuotaCode.Should().Be("image_generation");
        quotas[0].Limit.Should().Be(0, "plans have no monthly quotas, and 0 means no limit");
    }

    [TestMethod]
    public void Build_WithHeavyUsage_NeverWarns()
    {
        var usage = new UsageStatistic
        {
            Id = Guid.NewGuid(),
            UserId = SubscriptionTestData.UserId,
            BillingPeriodStart = DateOnly.FromDateTime(SubscriptionTestData.UtcNow.UtcDateTime),
            BillingPeriodEnd = DateOnly.FromDateTime(SubscriptionTestData.UtcNow.UtcDateTime).AddMonths(1),
            ImagesGenerated = 10_000
        };

        var quotas = QuotaDefinitions.Build(usage);

        quotas.Single().Used.Should().Be(10_000);
        quotas.Single().PercentUsed.Should().Be(0);
        quotas.Single().IsWarning.Should().BeFalse();
    }
}
