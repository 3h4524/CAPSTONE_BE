using APCS.Application.Abstractions.Commerce;
using APCS.Application.Features.Commerce;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class MarketResearchSelectorTests
{
    [TestMethod]
    public void Select_TakesTopNFromEachNicheInsteadOfOneGlobalTopN()
    {
        var listings = new[]
        {
            Listing("hiking-1", "hiking shirt", 100, 100),
            Listing("hiking-2", "hiking shirt", 90, 90),
            Listing("hiking-3", "hiking shirt", 80, 80),
            Listing("hiking-4", "hiking shirt", 70, 70),
            Listing("wedding-1", "wooden wedding gift", 40, 40),
            Listing("wedding-2", "wooden wedding gift", 30, 30),
            Listing("wedding-3", "wooden wedding gift", 20, 20),
            Listing("wedding-4", "wooden wedding gift", 10, 10),
        };

        var selected = MarketResearchSelector.Select(listings, topNPerNiche: 2, minimumViralScore: 0);

        selected.Should().HaveCount(4);
        selected.GroupBy(item => item.Listing.MatchedQuery).Should().OnlyContain(group => group.Count() == 2);
        selected.Select(item => item.Listing.ExternalListingId).Should().BeEquivalentTo(
            ["hiking-1", "hiking-2", "wedding-1", "wedding-2"]);
    }

    private static CommerceMarketListing Listing(string id, string niche, int favorites, int views) =>
        new(CommercePlatform.Etsy, id, "seller", id, "description", null, null, null, null, favorites, views, null, null, [], niche);
}
