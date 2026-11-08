using APCS.Application.Features.Commerce;
using APCS.Domain.Entities;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class MarketListingFilterTests
{
    private static MarketListingSnapshot Snapshot(string listingId, bool synthetic = false, string? rawPayload = null) => new()
    {
        Id = Guid.NewGuid(),
        Platform = "etsy",
        ExternalListingId = listingId,
        Title = $"Listing {listingId}",
        MatchedQuery = "embroidered sweatshirt",
        Tags = "[]",
        Favorites = 10,
        Views = 100,
        ObservedAt = new DateTime(2026, 11, 5),
        CollectedAt = new DateTime(2026, 11, 5),
        IsSynthetic = synthetic,
        RawPayload = rawPayload
    };

    [TestMethod]
    public void Apply_RejectsDigitalDownloads()
    {
        var result = MarketListingFilter.Apply([
            Snapshot("1", rawPayload: "{\"listing_type\": \"physical\"}"),
            Snapshot("2", rawPayload: "{\"listing_type\": \"download\", \"title\": \"Hoodie sewing pattern\"}"),
            Snapshot("3")
        ]);

        result.Eligible.Select(item => item.ExternalListingId).Should().Equal("1", "3");
        result.RejectionReasons.Should().ContainKey("digital_download").WhoseValue.Should().Be(1);
    }

    [TestMethod]
    [DataRow("{\"listing_type\": \"download\"}", true)]
    [DataRow("{\"listing_type\": \"DOWNLOAD\"}", true)]
    [DataRow("{\"listing_type\": \"physical\"}", false)]
    [DataRow("{}", false)]
    [DataRow("not json", false)]
    [DataRow(null, false)]
    public void IsDigital_ReadsTheEtsyListingType(string? rawPayload, bool expected)
    {
        MarketListingKind.IsDigital(rawPayload).Should().Be(expected);
    }

    [TestMethod]
    public void Apply_RejectsSyntheticDemoSnapshots()
    {
        var result = MarketListingFilter.Apply([Snapshot("1"), Snapshot("synthetic-etsy-apparel-3-1", synthetic: true)]);

        result.Eligible.Select(item => item.ExternalListingId).Should().Equal("1");
        result.RejectionReasons.Should().ContainKey("synthetic").WhoseValue.Should().Be(1);
    }
}
