using APCS.Application.Abstractions.Ai;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Commerce;
using APCS.Application.Features.Entitlements;
using APCS.Application.UnitTests.TestSupport;
using APCS.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MockQueryable.Moq;
using Moq;

namespace APCS.Application.UnitTests.Features.Commerce;

[TestClass]
public sealed class MarketRadarAccessTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly List<OpportunityAnalysisRun> runRows = [];
    private readonly Mock<IGeminiTextGenerationProvider> gemini = new();

    private static TestEntitlements Pro(int? cap) => TestEntitlements.Plan(features: new Dictionary<string, PlanFeatureValue>
    {
        [PlanFeatures.MarketRadarFull] = new(true, null),
        [PlanFeatures.DailyOpportunityAnalysisCap] = new(true, cap),
    });

    private static Mock<ICurrentUser> User()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.UserId).Returns(Owner);
        return user;
    }

    private static Mock<IRepository<T>> Rows<T>(List<T> rows) where T : class
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(x => x.Query()).Returns(() => rows.AsQueryable().BuildMock());
        repository.Setup(x => x.AddAsync(It.IsAny<T>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((T row, bool _, CancellationToken _) => rows.Add(row));
        return repository;
    }

    private MarketRadarAccess Access(TestEntitlements plan) => new(User().Object, plan, Rows(runRows).Object, clock);

    private OpportunityAnalysisRun Ran(DateTime at, string status = "succeeded") => new()
    {
        Id = Guid.NewGuid(), Status = status, PromptVersion = "v", SchemaVersion = "v", InputSnapshot = "{}", RequestedAt = at,
        SellerOpportunity = new SellerOpportunity { UserId = Owner },
    };

    [TestMethod]
    public async Task GetPlanAsync_OnFree_KeepsTheWholeRadarLocked()
    {
        var plan = await Access(TestEntitlements.Free()).GetPlanAsync(Owner);

        plan.Full.Should().BeFalse();
        plan.AnalysesPerDay.Should().Be(0);
    }

    [TestMethod]
    public async Task GetPlanAsync_OnPro_CountsTodaysAnalysesButNotFailedOrEarlierOnes()
    {
        var today = clock.GetUtcNow().UtcDateTime.Date;
        runRows.Add(Ran(today.AddHours(1)));
        runRows.Add(Ran(today.AddHours(2), "failed"));
        runRows.Add(Ran(today.AddDays(-1)));

        var plan = await Access(Pro(20)).GetPlanAsync(Owner);

        plan.Full.Should().BeTrue();
        plan.AnalysesPerDay.Should().Be(20);
        plan.AnalysesUsedToday.Should().Be(1);
    }

    [TestMethod]
    public async Task RequireAnalysisAsync_AtTodaysCap_IsRefused()
    {
        runRows.Add(Ran(clock.GetUtcNow().UtcDateTime.Date.AddHours(1)));

        var result = await Access(Pro(1)).RequireAnalysisAsync();

        result.Error.Code.Should().Be("Plan.DailyCapReached");
    }

    [TestMethod]
    public async Task RequireRefreshAsync_OnFree_IsOpenOncePerDayAndForAFirstRanking()
    {
        var rankings = new List<SellerOpportunity>();
        MarketRadarAccess Free() => new(User().Object, TestEntitlements.Free(), Rows(runRows).Object, clock, Rows(rankings).Object);

        (await Free().RequireRefreshAsync()).IsSuccess.Should().BeTrue("a radar never ranked is ranked at once");

        rankings.Add(new SellerOpportunity { UserId = Owner, LastEvaluatedAt = clock.GetUtcNow().UtcDateTime.AddHours(-2), FitBreakdown = "{}" });
        (await Free().RequireRefreshAsync()).Error.Code.Should().Be("Plan.FeatureLocked");

        rankings[0].LastEvaluatedAt = clock.GetUtcNow().UtcDateTime.AddHours(-25);
        (await Free().RequireRefreshAsync()).IsSuccess.Should().BeTrue("a day has passed");
    }

    [TestMethod]
    public async Task RequireFullAsync_OnFree_IsPlanLocked()
    {
        var result = await Access(TestEntitlements.Free()).RequireFullAsync();

        result.Error.Code.Should().Be("Plan.FeatureLocked");
    }

    [TestMethod]
    public void Limited_LeavesOutTheCompetitorsAndWhatToCopy_ButKeepsTheVerdict()
    {
        var niche = new SellerNicheMarketInsightResponse("cat tees", ["cat tee"], "promising", "Room to enter.", 12, "medium", "stable", null,
            "rising", 5, "rising", "good", 4, 0.8,
            [new("shop-1", 3, "rising", null, "Cat tee", "https://etsy.test/1", 20)],
            [new("cat tee", 50, 12, "medium", 70, 60, [], null, "rising", null, "good", "high", 300, MarketListingSignals.Empty, null)],
            "high", MarketSupplySignal.Unknown, MarketListingSignals.Empty, "Test a design.", ["Do this"], ["Watch that"], ["The top is open"]);

        var limited = MarketRadarAccess.Limited(niche);

        limited.Verdict.Should().Be("promising");
        limited.Conclusion.Should().Be("Room to enter.");
        limited.Highlights.Should().Equal("The top is open");
        limited.Competitors.Should().BeEmpty();
        limited.Keywords.Should().BeEmpty();
        limited.ActionPlan.Should().BeEmpty();
        limited.Risks.Should().BeEmpty();
    }

    [TestMethod]
    public void Limited_KeepsTheTopsAggregateNumbers_ButNotItsListings()
    {
        var signals = MarketListingSignals.Empty with
        {
            SampleListingCount = 48, SellerCount = 30, StrongSellerCount = 6, RisingNewListingCount = 3, MedianFavoritesPer30Days = 4.5,
            PriceP25 = 18m, MedianPrice = 22m, PriceP75 = 27m,
            Points = [new("Cat tee", 22m, 4.5, 20, true, "https://etsy.test/1")],
            RisingListings = [new("Cat tee", "https://etsy.test/1", 20, 9, 22m, "USD")],
        };
        var niche = new SellerNicheMarketInsightResponse("cat tees", ["cat tee"], "promising", "Room to enter.", 12, "medium", "stable", null,
            "rising", 5, "rising", "good", 4, 0.8, [], [], "high", MarketSupplySignal.Unknown, signals, "Test a design.", [], [], []);

        var limited = MarketRadarAccess.Limited(niche).Signals;

        limited.SampleListingCount.Should().Be(48);
        limited.StrongSellerCount.Should().Be(6);
        limited.RisingNewListingCount.Should().Be(3);
        limited.MedianFavoritesPer30Days.Should().Be(4.5);
        limited.Points.Should().BeEmpty();
        limited.RisingListings.Should().BeEmpty();
        limited.PriceP25.Should().BeNull();
        limited.PriceP75.Should().BeNull();
    }

    private OpportunityAnalysisService Analysis(IMarketRadarAccess access, List<SellerOpportunity> opportunities)
    {
        var unit = new Mock<IUnitOfWork>();
        return new(User().Object, Rows(opportunities).Object, Rows(new List<MarketOpportunityEvidence>()).Object, Rows(runRows).Object,
            unit.Object, gemini.Object, access);
    }

    private static List<SellerOpportunity> OneOpportunity(out Guid id)
    {
        id = Guid.NewGuid();
        return [new SellerOpportunity
        {
            Id = id, UserId = Owner, FitBreakdown = "{}",
            Opportunity = new MarketOpportunity { Platform = "etsy", Title = "Cat tees", Niche = "cat tees", MarketOpportunityScore = 80, Confidence = 0.7m },
        }];
    }

    private void GeminiAnswers() => gemini.Setup(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new GeminiTextGenerationResult(
            """{"summary":"S","recommendation":"test","supportingPoints":["p"],"risks":["r"],"actionPlan":["a","b","c"],"confidenceNote":"c"}""",
            "gemini-model", 1, 1, "req"));

    [TestMethod]
    public async Task AnalyzeAsync_TheSameEvidenceAgain_IsAnsweredWithoutAskingTheModelOrTheCap()
    {
        GeminiAnswers();
        var access = new Mock<IMarketRadarAccess>();
        access.Setup(x => x.RequireFullAsync(It.IsAny<CancellationToken>())).ReturnsAsync(APCS.Common.Models.Result.Success());
        access.Setup(x => x.RequireAnalysisAsync(It.IsAny<CancellationToken>())).ReturnsAsync(APCS.Common.Models.Result.Success());
        var service = Analysis(access.Object, OneOpportunity(out var id));

        var first = await service.AnalyzeAsync(id);
        var second = await service.AnalyzeAsync(id);

        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Message : null);
        second.Value.RunId.Should().Be(first.Value.RunId);
        gemini.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        access.Verify(x => x.RequireAnalysisAsync(It.IsAny<CancellationToken>()), Times.Once, "a reused analysis does not count");
    }

    [TestMethod]
    public async Task AnalyzeAsync_PastTodaysCap_IsRefusedWithoutAskingTheModel()
    {
        GeminiAnswers();
        var service = Analysis(Access(Pro(0)), OneOpportunity(out var id));

        var result = await service.AnalyzeAsync(id);

        result.Error.Code.Should().Be("Plan.DailyCapReached");
        gemini.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AnalyzeAsync_OnFree_IsPlanLocked()
    {
        var service = Analysis(Access(TestEntitlements.Free()), OneOpportunity(out var id));

        var result = await service.AnalyzeAsync(id);

        result.Error.Code.Should().Be("Plan.FeatureLocked");
        gemini.Verify(x => x.GenerateAsync(It.IsAny<GeminiTextGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
