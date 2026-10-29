using APCS.Application.Abstractions.Commerce;
using APCS.Application.Features.Commerce;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class ViralListingScorerTests
{
    [TestMethod]
    public void Score_WhenListingHasTheStrongestAvailableSignals_ReturnsOneHundred()
    {
        var listings = new[]
        {
            Listing("a", 10, 100),
            Listing("b", 1000, 10000)
        };

        var score = ViralListingScorer.Score(listings[1], listings);

        score.Should().Be(100);
    }

    [TestMethod]
    public void Score_WhenPlatformOmitsMetrics_ReturnsZeroWithoutFabricatingData()
    {
        var listing = Listing("a", null, null);

        ViralListingScorer.Score(listing, [listing]).Should().Be(0);
    }

    private static CommerceMarketListing Listing(string id, int? favorites, int? views) =>
        new(CommercePlatform.Etsy, id, "seller", id, "description", null, null, null, null, favorites, views, null, null, []);
}
