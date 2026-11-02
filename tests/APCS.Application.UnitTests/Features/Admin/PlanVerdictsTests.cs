using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class PlanVerdictsTests
{
    private static readonly PlanVerdictOptions Rules = new();

    [TestMethod]
    public void Build_APlanPricedFarAboveTheOneBelowButUsedNoMore_IsOverpriced()
    {
        // Studio costs 2.7× Pro, yet its typical Seller makes 12 designs and 24 mockups against Pro's 14 and 25.
        var pro = Plan("pro", "Pro", 749_750m, Sellers(25, designs: 14, mockups: 25));
        var studio = Plan("studio", "Studio", 1_999_750m, Sellers(25, designs: 12, mockups: 24));

        var studioVerdict = PlanVerdicts.Build([pro, studio], Rules).Single(verdict => verdict.Key == "studio");

        studioVerdict.Verdict.Should().Be("overpriced");
        studioVerdict.VerdictLabel.Should().Be("Overpriced vs Pro");
        studioVerdict.Evidence[0].Should().Be("Pays 2.7× Pro, but a typical Seller makes 12 designs and 24 mockups against Pro's 14 and 25.");
    }

    [TestMethod]
    public void Build_APricierPlanUsedMore_IsNotOverpriced()
    {
        var starter = Plan("starter", "Starter", 249_750m, Sellers(25, designs: 5, mockups: 8));
        var pro = Plan("pro", "Pro", 749_750m, Sellers(25, designs: 14, mockups: 25));

        PlanVerdicts.Build([starter, pro], Rules).Single(verdict => verdict.Key == "pro").Verdict.Should().Be("healthy");
    }

    [TestMethod]
    public void Build_ThePlanThatKeepsFewestCustomersBelowTheBar_IsARetentionRisk()
    {
        var starter = Plan("starter", "Starter", 249_750m, Sellers(25, designs: 5, mockups: 8), retentionStart: 43, retained: 14, rate: 32.6m);
        var pro = Plan("pro", "Pro", 749_750m, Sellers(25, designs: 5, mockups: 8), retentionStart: 32, retained: 12, rate: 37.5m);

        var verdicts = PlanVerdicts.Build([starter, pro], Rules);

        var starterVerdict = verdicts.Single(verdict => verdict.Key == "starter");
        starterVerdict.Verdict.Should().Be("retention_risk");
        starterVerdict.Evidence.Should().Contain("Keeps 32.6% of its customers (14 of 43), the lowest of all plans.");
        verdicts.Single(verdict => verdict.Key == "pro").Evidence.Should().Contain("Keeps 37.5% of its customers (12 of 32).");
    }

    [TestMethod]
    public void Build_SellersAlreadyUsingAsMuchAsTheNextPlan_AreUpsellCandidates()
    {
        // 3 Starter Sellers make 14 designs and 25 mockups, as many as a typical Pro Seller; 2 more
        // match only the designs, which is not enough.
        var starterSellers = Sellers(20, designs: 4, mockups: 6)
            .Concat(Sellers(3, designs: 14, mockups: 25))
            .Concat(Sellers(2, designs: 14, mockups: 6))
            .ToList();
        var starter = Plan("starter", "Starter", 249_750m, starterSellers);
        var pro = Plan("pro", "Pro", 749_750m, Sellers(25, designs: 14, mockups: 25));

        var starterVerdict = PlanVerdicts.Build([starter, pro], Rules).Single(verdict => verdict.Key == "starter");

        starterVerdict.Verdict.Should().Be("upsell");
        starterVerdict.UpsellCandidates.Should().Be(3);
        starterVerdict.Action.Should().Be("Offer Pro to the 3 heaviest Starter Sellers.");
    }

    [TestMethod]
    public void Build_NeverOffersAPlanThatIsNotOnSaleOrIsUsedNoMore()
    {
        // Studio is used no more than Pro, and Vip is not on sale: Pro has no upgrade to offer.
        var pro = Plan("pro", "Pro", 749_750m, Sellers(25, designs: 14, mockups: 25));
        var studio = Plan("studio", "Studio", 1_999_750m, Sellers(25, designs: 12, mockups: 24));
        var vip = Plan("vip", "Vip", 2_475_000m, Sellers(25, designs: 30, mockups: 50), onSale: false);

        var proVerdict = PlanVerdicts.Build([pro, studio, vip], Rules).Single(verdict => verdict.Key == "pro");

        proVerdict.Verdict.Should().Be("healthy");
        proVerdict.UpsellCandidates.Should().Be(0);
    }

    [TestMethod]
    public void Build_AHealthyPlan_SuggestsPromotingItsLeastUsedFeature()
    {
        var adoption = new List<FeatureAdoptionDto>
        {
            new("design_images", "Design images", true, 20, 80m),
            new("uploaded_mockups", "Uploaded mockups", true, 5, 22.7m),
            new("ai_videos", "AI videos", false, 0, 0m),
        };
        var pro = Plan("pro", "Pro", 749_750m, Sellers(25, designs: 14, mockups: 25), adoption: adoption);

        var verdict = PlanVerdicts.Build([pro], Rules).Single();

        verdict.Verdict.Should().Be("healthy");
        verdict.Evidence.Should().Contain("Uploaded mockups is used by only 22.7% of its Sellers.");
        verdict.Action.Should().Be("Show Uploaded mockups to Sellers who have not tried it.");
    }

    [TestMethod]
    public void Build_UsesTheConfiguredThresholds()
    {
        // 72.7% retention is fine against the default bar of 60%, but not against a bar of 75%.
        var starter = Plan("starter", "Starter", 249_750m, Sellers(25, designs: 5, mockups: 8), retentionStart: 77, retained: 56, rate: 72.7m);

        PlanVerdicts.Build([starter], Rules).Single().Verdict.Should().Be("healthy");
        PlanVerdicts.Build([starter], new PlanVerdictOptions { LowRetentionRate = 75m }).Single().Verdict.Should().Be("retention_risk");
    }

    [TestMethod]
    public void Build_APlanBelowTheMinimumSample_IsNotJudged()
    {
        var vip = Plan("vip", "Vip", 2_475_000m, Sellers(3, designs: 1, mockups: 1), hasEnoughData: false);

        var verdict = PlanVerdicts.Build([vip], Rules).Single();

        verdict.Verdict.Should().Be("not_enough_data");
        verdict.Evidence.Should().Equal("3 subscribers, at least 20 needed to judge.");
        verdict.Action.Should().BeNull();
    }

    private static List<SellerUsage> Sellers(int count, int designs, int mockups) =>
        Enumerable.Range(0, count).Select(_ => new SellerUsage(designs, mockups)).ToList();

    private static PlanVerdictInput Plan(
        string key,
        string name,
        decimal price,
        IReadOnlyList<SellerUsage> sellers,
        int retentionStart = 0,
        int retained = 0,
        decimal? rate = null,
        bool hasEnoughData = true,
        IReadOnlyList<FeatureAdoptionDto>? adoption = null,
        bool onSale = true) =>
        new(key, name, onSale, sellers.Count, hasEnoughData, price, adoption ?? [], sellers, retentionStart, retained, rate);
}
