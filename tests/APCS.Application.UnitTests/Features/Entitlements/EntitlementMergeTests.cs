using APCS.Application.Features.Entitlements;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Entitlements;

[TestClass]
public sealed class EntitlementMergeTests
{
    private static SubscriptionFeatureGrant Kept(string code, bool isEnabled = true, int? limit = null) => new()
    {
        FeatureCode = code,
        IsEnabled = isEnabled,
        LimitValue = limit,
        Reason = EntitlementMerge.FeatureRemoved,
    };

    private static Dictionary<string, PlanFeatureValue> Plan(params (string Code, bool IsEnabled, int? Limit)[] features) =>
        features.ToDictionary(feature => feature.Code, feature => new PlanFeatureValue(feature.IsEnabled, feature.Limit));

    [TestMethod]
    public void Merge_WithNoGrants_IsThePlan()
    {
        var plan = Plan((PlanFeatures.AiVideo, true, null));

        EntitlementMerge.Merge(plan, []).Should().BeEquivalentTo(plan);
    }

    [TestMethod]
    public void Merge_AFlagIsOnWhenThePlanOrTheGrantHasIt()
    {
        var merged = EntitlementMerge.Merge(
            Plan((PlanFeatures.AiVideo, false, null), (PlanFeatures.AutoApproval, true, null)),
            [Kept(PlanFeatures.AiVideo), Kept(PlanFeatures.LifestyleMockup), Kept(PlanFeatures.AutoApproval, isEnabled: false)]);

        merged[PlanFeatures.AiVideo].IsEnabled.Should().BeTrue("the plan switched it off");
        merged[PlanFeatures.LifestyleMockup].IsEnabled.Should().BeTrue("the plan no longer has a row for it");
        merged[PlanFeatures.AutoApproval].IsEnabled.Should().BeTrue("a grant never takes away what the plan gives");
    }

    [TestMethod]
    [DataRow(2, 4, true, 4, DisplayName = "a higher kept limit wins")]
    [DataRow(6, 4, true, 6, DisplayName = "a plan raised since keeps its own limit")]
    [DataRow(2, null, true, null, DisplayName = "a kept 'no limit' wins")]
    public void Merge_ALimitTakesTheHigherValue(int? planLimit, int? keptLimit, bool keptEnabled, int? expected)
    {
        var merged = EntitlementMerge.Merge(
            Plan((PlanFeatures.DesignVariations, true, planLimit)),
            [Kept(PlanFeatures.DesignVariations, keptEnabled, keptLimit)]);

        merged[PlanFeatures.DesignVariations].LimitValue.Should().Be(expected);
    }

    [TestMethod]
    public void Merge_ALimitSwitchedOffComesBackFromTheGrant()
    {
        var merged = EntitlementMerge.Merge(
            Plan((PlanFeatures.DailyMockupCap, false, 300)),
            [Kept(PlanFeatures.DailyMockupCap, limit: 300)]);

        merged[PlanFeatures.DailyMockupCap].Should().Be(new PlanFeatureValue(true, 300));
    }

    [TestMethod]
    public void Merge_ALimitWithNoPlanRowStaysUnlimited()
    {
        var merged = EntitlementMerge.Merge(Plan(), [Kept(PlanFeatures.DailyVideoCap, limit: 50)]);

        merged.Should().NotContainKey(PlanFeatures.DailyVideoCap, "no row already means no cap");
    }
}
