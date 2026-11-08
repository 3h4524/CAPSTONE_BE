using APCS.Application.Features.Commerce;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class MarketCategoryInferenceTests
{
    [TestMethod]
    [DataRow("personalized t shirt", "apparel")]
    [DataRow("custom hoodie", "apparel")]
    [DataRow("baby name sweatshirt", "apparel")]
    [DataRow("crochet cardigan", "apparel")]
    [DataRow("embroidered tote bag", "accessories")]
    [DataRow("custom baseball cap", "accessories")]
    [DataRow("denim jacket patch", "accessories")]
    [DataRow("linen apron", "accessories")]
    [DataRow("wall art", null)]
    public void FromText_ReadsTheProductTypeFromTheNiche(string niche, string? expected)
    {
        MarketCategoryInference.FromText(niche).Should().Be(expected);
    }

    [TestMethod]
    public void Resolve_KeepsAStoredCategoryAndInfersAMissingOne()
    {
        MarketCategoryInference.Resolve(new MarketOpportunity { Niche = "custom hoodie", Title = "Hoodie", Category = "Personalized Gifts" })
            .Should().Be("Personalized Gifts");
        MarketCategoryInference.Resolve(new MarketOpportunity { Niche = "custom hoodie", Title = "Hoodie", Category = null })
            .Should().Be(MarketCategoryInference.Apparel);
    }

    [TestMethod]
    public void Score_DoesNotPunishALongRealListingTitle()
    {
        // A real Etsy title is 20-30 words; keyword fit counts the seller's keywords it contains.
        var words = "embroidered sweatshirt Velaris embroidered sweatshirt, Velaris City Of Starlight Embroidered Shirt, The Night Court Shirt, SJM sweater, City of Starlight, ACOTAR T-Shirt"
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var opportunity = new MarketOpportunityCandidate(80, 0.5, MarketCategoryInference.Apparel, words, 30);
        var profile = new SellerResearchProfileSnapshot(
            ["apparel"], ["t-shirt", "sweatshirt", "embroidered", "graphic tee", "hoodie"], ["apparel"], [], 20, 80);

        var result = SellerOpportunityScorer.Score(opportunity, profile);

        result.Breakdown["keywordFit"].Should().Be(100);
        result.Breakdown["categoryFit"].Should().Be(100);
    }

    [TestMethod]
    public void Score_GivesPartialKeywordFitForOneMatchedKeyword()
    {
        var opportunity = new MarketOpportunityCandidate(80, 0.5, MarketCategoryInference.Apparel, ["vintage", "graphic", "tees", "for", "dad"], 30);
        var profile = new SellerResearchProfileSnapshot([], ["graphic tee", "hoodie", "wedding", "pet"], [], [], 20, 80);

        SellerOpportunityScorer.Score(opportunity, profile).Breakdown["keywordFit"].Should().BeApproximately(33.33, 0.01);
    }
}
