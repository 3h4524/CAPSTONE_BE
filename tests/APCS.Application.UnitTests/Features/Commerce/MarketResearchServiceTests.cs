using System.Linq.Expressions;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Rag;
using APCS.Application.Features.Commerce;
using APCS.Application.Features.Rag.Dtos;
using APCS.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class MarketResearchServiceTests
{
    private const string Niche = "cat shirt";
    private readonly List<MarketListingSnapshot> snapshotRows = [];
    private readonly List<MarketOpportunity> opportunityRows = [];
    private readonly List<MarketOpportunityEvidence> evidenceRows = [];
    private readonly List<MarketQueryObservation> observationRows = [];
    private readonly Mock<ICommerceMarketDataConnector> connector = new();
    private readonly Mock<IMarketListingQueries> listingQueries = new();

    private MarketResearchService Create()
    {
        var factory = new Mock<ICommerceMarketDataConnectorFactory>();
        factory.Setup(x => x.Create(CommercePlatform.Etsy)).Returns(connector.Object);
        var rag = new Mock<IRagService>();
        rag.Setup(x => x.IngestAsync(It.IsAny<RagIngestRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Guid.NewGuid());

        var snapshots = new Mock<IRepository<MarketListingSnapshot>>();
        snapshots.Setup(x => x.Query()).Returns(() => snapshotRows.AsQueryable().BuildMock());
        snapshots.Setup(x => x.AddAsync(It.IsAny<MarketListingSnapshot>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((MarketListingSnapshot row, bool _, CancellationToken _) => snapshotRows.Add(row));
        snapshots.Setup(x => x.FindAsync(It.IsAny<Expression<Func<MarketListingSnapshot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<MarketListingSnapshot, bool>> predicate, CancellationToken _) => snapshotRows.Where(predicate.Compile()).ToList());

        var opportunities = new Mock<IRepository<MarketOpportunity>>();
        opportunities.Setup(x => x.Query()).Returns(() => opportunityRows.AsQueryable().BuildMock());
        opportunities.Setup(x => x.FindAsync(It.IsAny<Expression<Func<MarketOpportunity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<MarketOpportunity, bool>> predicate, CancellationToken _) => opportunityRows.Where(predicate.Compile()).ToList());
        opportunities.Setup(x => x.AddAsync(It.IsAny<MarketOpportunity>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((MarketOpportunity row, bool _, CancellationToken _) => opportunityRows.Add(row));

        var evidence = new Mock<IRepository<MarketOpportunityEvidence>>();
        evidence.Setup(x => x.Query()).Returns(() => evidenceRows.AsQueryable().BuildMock());
        evidence.Setup(x => x.AddAsync(It.IsAny<MarketOpportunityEvidence>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((MarketOpportunityEvidence row, bool _, CancellationToken _) =>
            {
                // As the database would: the row reaches its snapshot and opportunity.
                row.Snapshot = snapshotRows.Single(snapshot => snapshot.Id == row.SnapshotId);
                row.Opportunity = opportunityRows.Single(opportunity => opportunity.Id == row.OpportunityId);
                evidenceRows.Add(row);
            });

        var observations = new Mock<IRepository<MarketQueryObservation>>();
        observations.Setup(x => x.Query()).Returns(() => observationRows.AsQueryable().BuildMock());
        observations.Setup(x => x.FindAsync(It.IsAny<Expression<Func<MarketQueryObservation, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<MarketQueryObservation, bool>> predicate, CancellationToken _) => observationRows.Where(predicate.Compile()).ToList());

        return new MarketResearchService(factory.Object, rag.Object, snapshots.Object, opportunities.Object, evidence.Object,
            observations.Object, Mock.Of<IUnitOfWork>(), listingQueries.Object);
    }

    private static CommerceMarketListing Listing(string id, string title, int favorites, int views, string seller, params string[] tags) =>
        new(CommercePlatform.Etsy, id, seller, title, "A tee.", $"https://etsy.test/{id}", 24.99m, "USD", 10, favorites, views,
            null, null, tags.Length == 0 ? ["cat"] : tags, Niche, 1);

    private void Collect(params CommerceMarketListing[] listings) =>
        connector.Setup(x => x.SearchPublicListingsAsync(It.IsAny<CommerceMarketSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceMarketSearchResult(listings, [new CommerceMarketQueryResult(Niche, 1000, listings.Length, 100, "score")]));

    /// <summary>An opportunity backed by one listing, observed <paramref name="daysAgo"/> days ago.</summary>
    private MarketOpportunity Known(string listingId, string title, int daysAgo, string status = "active", params string[] tags)
    {
        var observed = DateTime.UtcNow.AddDays(-daysAgo);
        var opportunity = new MarketOpportunity
        {
            Id = Guid.NewGuid(), Platform = "etsy", Title = title, Niche = Niche, MarketOpportunityScore = 80m, Confidence = 0.5m,
            ScoreBreakdown = "{}", FormulaVersion = "viral-v1", Status = status, FirstObservedAt = observed, LastObservedAt = observed,
            CreatedAt = observed, UpdatedAt = observed,
        };
        var snapshot = new MarketListingSnapshot
        {
            Id = Guid.NewGuid(), Platform = "etsy", ExternalListingId = listingId, ExternalSellerId = "shop-1", Title = title,
            Tags = System.Text.Json.JsonSerializer.Serialize(tags),
            MatchedQuery = Niche, ObservedAt = observed, CollectedAt = observed,
        };
        opportunityRows.Add(opportunity);
        snapshotRows.Add(snapshot);
        evidenceRows.Add(new MarketOpportunityEvidence { OpportunityId = opportunity.Id, SnapshotId = snapshot.Id, Snapshot = snapshot, Opportunity = opportunity, CreatedAt = observed });
        return opportunity;
    }

    [TestMethod]
    public async Task RetrieveAndIngestAsync_AKnownListingOutOfTheTopPicks_StillBacksItsOpportunity_AndRevivesIt()
    {
        var known = Known("old-1", "Grumpy cat tee with coffee", daysAgo: 20, status: "stale");
        Collect(
            Listing("hot-1", "Space cat galaxy shirt", favorites: 900, views: 3000, seller: "shop-2"),
            Listing("old-1", "Grumpy cat tee with coffee", favorites: 2, views: 400, seller: "shop-1"));

        await Create().RetrieveAndIngestAsync(new MarketResearchRequest(CommercePlatform.Etsy, [Niche], TopN: 1));

        evidenceRows.Count(row => row.OpportunityId == known.Id).Should().Be(2, "today's snapshot backs it too");
        known.Status.Should().Be("active");
        known.LastObservedAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-5));
        known.Confidence.Should().Be(MarketOpportunityLifecycle.Confidence(1, 2, 1, 2, 0), "it is recomputed from both observed days");
    }

    [TestMethod]
    public async Task RetrieveAndIngestAsync_ARepeatPick_BlendsItsScore_InsteadOfKeepingTheBestDay()
    {
        var known = Known("old-1", "Space cat galaxy shirt", daysAgo: 1);
        known.MarketOpportunityScore = 100m;
        Collect(
            Listing("old-1", "Space cat galaxy shirt", favorites: 10, views: 3000, seller: "shop-1"),
            Listing("hot-2", "Cat mom floral tee", favorites: 900, views: 1000, seller: "shop-2"));

        await Create().RetrieveAndIngestAsync(new MarketResearchRequest(CommercePlatform.Etsy, [Niche], TopN: 2));

        known.MarketOpportunityScore.Should().BeLessThan(100m);
        evidenceRows.Count(row => row.OpportunityId == known.Id).Should().Be(2, "one snapshot is one piece of evidence, not two");
    }

    [TestMethod]
    public async Task RetrieveAndIngestAsync_CollectedTwiceTheSameDay_KeepsOneSnapshotAndOneEvidence_WithTheLatestFigures()
    {
        var service = Create();
        Collect(Listing("hot-1", "Space cat galaxy shirt", favorites: 900, views: 3000, seller: "shop-2"));
        await service.RetrieveAndIngestAsync(new MarketResearchRequest(CommercePlatform.Etsy, [Niche], TopN: 1));
        var opportunity = opportunityRows.Single();
        var scoreAfterFirst = opportunity.MarketOpportunityScore;

        // Another worker on the shared database collects the same query later that day.
        Collect(Listing("hot-1", "Space cat galaxy shirt", favorites: 950, views: 3100, seller: "shop-2"));
        await service.RetrieveAndIngestAsync(new MarketResearchRequest(CommercePlatform.Etsy, [Niche], TopN: 1));

        snapshotRows.Should().ContainSingle().Which.Favorites.Should().Be(950);
        evidenceRows.Should().ContainSingle();
        opportunity.MarketOpportunityScore.Should().Be(scoreAfterFirst, "the score moves once a day");
    }

    [TestMethod]
    public async Task RetrieveAndIngestAsync_SlimsSnapshotsOlderThanAWeek_InBatches_UntilNoneAreLeft()
    {
        listingQueries.SetupSequence(x => x.CompactSupersededSnapshotsAsync(It.IsAny<DateTime>(), MarketSnapshotRetention.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MarketSnapshotRetention.BatchSize)
            .ReturnsAsync(12);
        Collect(Listing("hot-1", "Space cat galaxy shirt", favorites: 900, views: 3000, seller: "shop-2"));

        await Create().RetrieveAndIngestAsync(new MarketResearchRequest(CommercePlatform.Etsy, [Niche], TopN: 1));

        listingQueries.Verify(x => x.CompactSupersededSnapshotsAsync(DateTime.UtcNow.Date.AddDays(-MarketSnapshotRetention.FullTextDays),
            MarketSnapshotRetention.BatchSize, It.IsAny<CancellationToken>()), Times.Exactly(2), "a short batch means nothing is left");
    }

    private static readonly string[] GrumpyTags = ["grumpy cat", "coffee lover", "monday mood", "sarcastic tee"];

    [TestMethod]
    public async Task RetrieveAndIngestAsync_AListingOutOfTheTopPicksShowingAKnownConcept_BacksIt()
    {
        var concept = Known("old-1", "Grumpy cat tee with coffee", daysAgo: 1, tags: GrumpyTags);
        Collect(
            Listing("hot-1", "Space cat galaxy shirt", favorites: 900, views: 3000, seller: "shop-2", "space cat", "galaxy"),
            Listing("new-1", "Not Today Cat Shirt, Funny Mug Lover Tee", favorites: 3, views: 300, seller: "shop-3", "grumpy cat", "coffee lover", "monday mood"),
            Listing("new-2", "Cute cat mom shirt", favorites: 2, views: 200, seller: "shop-4", "grumpy cat", "cat mom"));

        await Create().RetrieveAndIngestAsync(new MarketResearchRequest(CommercePlatform.Etsy, [Niche], TopN: 1));

        var backing = evidenceRows.Where(row => row.OpportunityId == concept.Id).Select(row => row.Snapshot.ExternalListingId).ToArray();
        backing.Should().Contain("new-1", "it shares three distinctive tags").And.NotContain("new-2", "one tag is not the concept");
        concept.Confidence.Should().Be(MarketOpportunityLifecycle.Confidence(2, 2, 2, 2, 0), "two listings from two shops back it now");
    }

    [TestMethod]
    public async Task RetrieveAndIngestAsync_ListingsThatOnlyJoinedAConcept_DoNotWidenIt()
    {
        var concept = Known("old-1", "Grumpy cat tee with coffee", daysAgo: 1, tags: GrumpyTags);
        // A listing that joined earlier carries other tags; the concept is still read from the listing it was named after.
        var joined = new MarketListingSnapshot
        {
            Id = Guid.NewGuid(), Platform = "etsy", ExternalListingId = "joined-1", ExternalSellerId = "shop-9", Title = "Monday mood mug tee",
            Tags = System.Text.Json.JsonSerializer.Serialize(new[] { "grumpy cat", "coffee lover", "monday mood", "cat mom", "plant lady", "book club" }),
            MatchedQuery = Niche, ObservedAt = DateTime.UtcNow.AddDays(-1), CollectedAt = DateTime.UtcNow.AddDays(-1),
        };
        snapshotRows.Add(joined);
        evidenceRows.Add(new MarketOpportunityEvidence { OpportunityId = concept.Id, SnapshotId = joined.Id, Snapshot = joined, Opportunity = concept });
        Collect(
            Listing("hot-1", "Space cat galaxy shirt", favorites: 900, views: 3000, seller: "shop-2", "space cat", "galaxy"),
            Listing("new-1", "Cat mom plant lady book club tee", favorites: 3, views: 300, seller: "shop-3", "cat mom", "plant lady", "book club"));

        await Create().RetrieveAndIngestAsync(new MarketResearchRequest(CommercePlatform.Etsy, [Niche], TopN: 1));

        evidenceRows.Where(row => row.OpportunityId == concept.Id).Select(row => row.Snapshot.ExternalListingId).Should().NotContain("new-1");
    }

    [TestMethod]
    public async Task RetrieveAndIngestAsync_ATopListingOfAKnownConcept_AddsToIt_InsteadOfOpeningAnother()
    {
        var concept = Known("old-1", "Grumpy cat tee with coffee", daysAgo: 1, tags: GrumpyTags);
        Collect(Listing("hot-1", "Not Today Cat Shirt, Funny Mug Lover Tee", favorites: 900, views: 3000, seller: "shop-2", GrumpyTags));

        await Create().RetrieveAndIngestAsync(new MarketResearchRequest(CommercePlatform.Etsy, [Niche], TopN: 1));

        opportunityRows.Should().ContainSingle().Which.Should().BeSameAs(concept);
        evidenceRows.Count(row => row.OpportunityId == concept.Id).Should().Be(2);
    }

    [TestMethod]
    public async Task RetrieveAndIngestAsync_AnOpportunityNotSeenForWeeks_GoesStale_ThenArchived()
    {
        var aging = Known("gone-1", "Pumpkin spice cat tee", daysAgo: 20);
        var gone = Known("gone-2", "Spooky cat ghost tee", daysAgo: 50, status: "stale");
        var fresh = Known("fresh-1", "Cat dad tee", daysAgo: 3);
        Collect(Listing("hot-1", "Space cat galaxy shirt", favorites: 900, views: 3000, seller: "shop-2"));

        await Create().RetrieveAndIngestAsync(new MarketResearchRequest(CommercePlatform.Etsy, [Niche], TopN: 1));

        aging.Status.Should().Be("stale");
        gone.Status.Should().Be("archived");
        fresh.Status.Should().Be("active");
    }
}
