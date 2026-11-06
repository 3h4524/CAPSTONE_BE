using APCS.Application.Features.SellerDashboard.Common;
using APCS.Application.Features.SellerDashboard.Dtos.Response;
using FluentAssertions;

namespace APCS.Application.UnitTests.Features.SellerDashboard;

[TestClass]
public sealed class DashboardInsightsTests
{
    private static readonly KpiDto Quiet = new(0, 0, null);

    private static DashboardKpisDto Kpis(KpiDto? success = null, KpiDto? cost = null) =>
        new(Quiet, success ?? Quiet, Quiet, cost ?? Quiet);

    private static IReadOnlyList<InsightDto> Build(
        DashboardKpisDto? kpis = null,
        IReadOnlyList<NicheOutcome>? niches = null,
        (string Message, int Count, int TotalFailed)? topFailure = null) =>
        DashboardInsights.Build(kpis ?? Kpis(), niches ?? [], topFailure);

    [TestMethod]
    public void Build_WhenThePeriodIsQuiet_SaysNothing() => Build().Should().BeEmpty();

    [TestMethod]
    [DataRow(90, 80, true)]
    [DataRow(90, 81, false)]
    public void Build_WarnsOnlyWhenTheSuccessRateFellTenPoints(int before, int now, bool warns)
    {
        var insights = Build(Kpis(success: new KpiDto(now, before, null)));

        insights.Any(x => x.Title == $"Success rate fell from {before}% to {now}%").Should().Be(warns);
    }

    [TestMethod]
    public void Build_WhenThereWasNoEarlierRate_DoesNotCompare() =>
        Build(Kpis(success: new KpiDto(10, 0, null))).Should().BeEmpty();

    [TestMethod]
    public void Build_NamesTheUsualFailureOnceItRepeats()
    {
        Build(topFailure: ("Timeout", 1, 4)).Should().BeEmpty();

        var insight = Build(topFailure: ("Timeout", 3, 4)).Should().ContainSingle().Subject;

        insight.Tone.Should().Be(DashboardInsights.Warning);
        insight.Detail.Should().Be("3 of the 4 failed products stopped for this reason.");
    }

    [TestMethod]
    public void Build_ShortensALongFailureMessage()
    {
        var insight = Build(topFailure: (new string('x', 80), 2, 2)).Should().ContainSingle().Subject;

        insight.Title.Should().Be($"Most failures: \"{new string('x', 57)}…\"");
    }

    [TestMethod]
    public void Build_IgnoresNichesWithTooFewProducts() =>
        Build(niches: [new NicheOutcome("Cats", DashboardInsights.MinNicheProducts - 1, 4)]).Should().BeEmpty();

    [TestMethod]
    public void Build_FlagsTheWorstNicheAndPraisesTheBest()
    {
        var insights = Build(niches: [new NicheOutcome("Cats", 8, 4), new NicheOutcome("Dogs", 10, 1)]);

        insights.Select(x => (x.Tone, x.Title)).Should().Equal(
            (DashboardInsights.Warning, "Cats fails often (50%)"),
            (DashboardInsights.Good, "Dogs is your most reliable niche"));
    }

    [TestMethod]
    public void Build_DoesNotPraiseTheNicheItWarnedAbout()
    {
        var insight = Build(niches: [new NicheOutcome("Cats", 8, 4)]).Should().ContainSingle().Subject;

        insight.Tone.Should().Be(DashboardInsights.Warning);
    }

    [TestMethod]
    public void Build_WhenEveryNicheFailsOften_PraisesNone()
    {
        var insights = Build(niches: [new NicheOutcome("Cats", 8, 4), new NicheOutcome("Dogs", 8, 3)]);

        insights.Should().ContainSingle().Which.Title.Should().StartWith("Cats");
    }

    [TestMethod]
    [DataRow(20, "AI cost per product rose 20%", DashboardInsights.Warning)]
    [DataRow(-20, "AI cost per product fell 20%", DashboardInsights.Good)]
    public void Build_PointsOutACostMove(int change, string title, string tone)
    {
        var insight = Build(Kpis(cost: new KpiDto(0.12m, 0.1m, change))).Should().ContainSingle().Subject;

        insight.Title.Should().Be(title);
        insight.Tone.Should().Be(tone);
    }

    [TestMethod]
    public void Build_IgnoresASmallCostMove() =>
        Build(Kpis(cost: new KpiDto(0.11m, 0.1m, 19))).Should().BeEmpty();

    [TestMethod]
    public void Build_KeepsTheMostPressingThree()
    {
        var insights = Build(
            Kpis(new KpiDto(50, 90, null), new KpiDto(0.2m, 0.1m, 100)),
            [new NicheOutcome("Cats", 8, 4), new NicheOutcome("Dogs", 10, 1)],
            ("Timeout", 3, 4));

        insights.Should().HaveCount(DashboardInsights.MaxInsights);
        insights.Should().OnlyContain(x => x.Tone == DashboardInsights.Warning);
        insights[0].Title.Should().StartWith("Success rate fell");
    }
}
