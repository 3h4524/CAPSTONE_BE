using APCS.Application.Features.Commerce;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class MarketOpportunityLifecycleTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0);

    [TestMethod]
    public void StatusFor_AgesUnobservedOpportunities_StaleThenArchived()
    {
        MarketOpportunityLifecycle.StatusFor("active", Now.AddDays(-13), Now).Should().Be("active");
        MarketOpportunityLifecycle.StatusFor("active", Now.AddDays(-14), Now).Should().Be("stale");
        MarketOpportunityLifecycle.StatusFor("stale", Now.AddDays(-44), Now).Should().Be("stale");
        MarketOpportunityLifecycle.StatusFor("active", Now.AddDays(-45), Now).Should().Be("archived");
        MarketOpportunityLifecycle.StatusFor("archived", Now.AddDays(-20), Now).Should().Be("archived", "only a new observation brings it back");
    }

    [TestMethod]
    public void BlendScore_ACoolingConcept_LosesScore_InsteadOfKeepingItsBestDay()
    {
        MarketOpportunityLifecycle.BlendScore(90m, 40).Should().Be(65m);
        MarketOpportunityLifecycle.BlendScore(40m, 90).Should().Be(65m);
        MarketOpportunityLifecycle.BlendScore(99m, 140).Should().Be(100m);
    }

    [TestMethod]
    public void Confidence_GrowsWithListingsDaysAndShops_AndStaysLowOnDemoData()
    {
        var thin = MarketOpportunityLifecycle.Confidence(1, 1, 1, 1, 0);
        var supported = MarketOpportunityLifecycle.Confidence(5, 7, 3, 20, 0);
        var demo = MarketOpportunityLifecycle.Confidence(5, 7, 3, 0, 20);

        supported.Should().Be(1m);
        thin.Should().BeLessThan(0.5m);
        demo.Should().BeLessThan(supported);
        MarketOpportunityLifecycle.Confidence(0, 0, 0, 0, 0).Should().Be(0.2m);
    }
}
