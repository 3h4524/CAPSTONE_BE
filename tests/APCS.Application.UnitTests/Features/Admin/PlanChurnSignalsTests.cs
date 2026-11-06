using System;
using System.Collections.Generic;
using System.Linq;
using APCS.Application.Features.Admin.SubscriptionPlans;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.Revenues;
using APCS.Application.Features.Revenues.Dtos;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Admin;

[TestClass]
public sealed class PlanChurnSignalsTests
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 11, 29);
    private static readonly DateOnly LeftOn = new(2026, 10, 31);

    [TestMethod]
    public void Build_ComparesFeatureUseOfThoseWhoStayedWithThoseWhoLeft()
    {
        var stayed = Users(20);
        var left = Users(10);
        // Design images: 16 of 20 who stayed (80%), 2 of 10 who left (20%).
        var uses = stayed.Take(16).Concat(left.Take(2)).Select(userId => Use(userId, new DateOnly(2026, 10, 5))).ToList();

        var signal = Build(stayed, left, [Feature("design_images", uses)]);

        signal.HasEnoughData.Should().BeTrue();
        var feature = signal.Features.Single();
        feature.StayedRate.Should().Be(80m);
        feature.LeftRate.Should().Be(20m);
    }

    [TestMethod]
    public void Build_CountsALeaversUseOnlyBeforeTheyLeft()
    {
        var stayed = Users(10);
        var left = Users(10);
        // Every leaver used it, but only after leaving.
        var uses = left.Select(userId => Use(userId, LeftOn.AddDays(3))).ToList();

        var signal = Build(stayed, left, [Feature("design_images", uses)]);

        signal.Features.Single().LeftRate.Should().Be(0m);
    }

    [TestMethod]
    public void Build_MeasuresHowMuchLeaversCutBackInTheirLast30Days()
    {
        var stayed = Users(10);
        var left = Users(10);
        // 10 uses 31–60 days before leaving, 4 in the last 30: (4 - 10) / 10 = -60%.
        var uses = left.Select(userId => Use(userId, LeftOn.AddDays(-45)))
            .Concat(left.Take(4).Select(userId => Use(userId, LeftOn.AddDays(-10))))
            .ToList();

        var signal = Build(stayed, left, [Feature("mockups", uses)]);

        signal.Features.Single().UsageChangeBeforeLeaving.Should().Be(-60m);
        signal.UsageChangeBeforeLeaving.Should().Be(-60m);
    }

    [TestMethod]
    public void Build_WithFewEarlierUses_GivesNoChangeBeforeLeaving()
    {
        var stayed = Users(10);
        var left = Users(10);
        // 9 uses 31–60 days before leaving, below the minimum of 10.
        var uses = left.Take(PlanChurnSignals.MinimumUses - 1).Select(userId => Use(userId, LeftOn.AddDays(-45))).ToList();

        var signal = Build(stayed, left, [Feature("mockups", uses)]);

        signal.Features.Single().UsageChangeBeforeLeaving.Should().BeNull();
    }

    [TestMethod]
    public void Build_GivesTheFailureRateOfEachGroupOnlyWithEnoughAttempts()
    {
        var stayed = Users(10);
        var left = Users(10);
        // Those who stayed: 20 attempts, 2 failed (10%). Those who left: 19 attempts, too few.
        var attempts = stayed.Concat(stayed)
            .Select((userId, index) => new SellerAttempt(userId, At(new DateOnly(2026, 10, 1)), index < 2))
            .Concat(left.Concat(left).Take(19).Select(userId => new SellerAttempt(userId, At(new DateOnly(2026, 10, 1)), true)))
            .ToList();

        var signal = Build(stayed, left, [Feature("listings", [], attempts)]);

        signal.Features.Single().StayedFailureRate.Should().Be(10m);
        signal.Features.Single().LeftFailureRate.Should().BeNull();
    }

    [TestMethod]
    public void Build_ProfilesThoseWhoLeft()
    {
        var stayed = Users(10);
        var left = Users(10);
        // 4 of 10 left within a month of first paying.
        var leftCustomers = left
            .Select((userId, index) => new LeftCustomerDto(userId, index < 4 ? LeftOn.AddDays(-20) : LeftOn.AddDays(-200), LeftOn))
            .ToList();
        // Those who stayed use mockups most, then design images, and listings least: the first two are core.
        // 3 of 10 who left made a design image; all 10 made a listing, which is not core: 7 of 10 never started.
        var day = new DateOnly(2026, 10, 1);
        var mockups = stayed.SelectMany(userId => Enumerable.Range(0, 3).Select(_ => Use(userId, day))).ToList();
        var designs = stayed.Select(userId => Use(userId, day)).Concat(left.Take(3).Select(userId => Use(userId, day))).ToList();
        var listings = stayed.Take(2).Concat(left).Select(userId => Use(userId, day)).ToList();

        var signal = PlanChurnSignals.Build("pro", "Pro", new PlanCohortDto("Pro", stayed, leftCustomers), From, To,
            [Feature("design_images", designs), Feature("mockups", mockups), Feature("listings", listings)]);

        signal.LeftInFirstMonthRate.Should().Be(40m);
        signal.CoreFeatures.Should().Equal("mockups", "design_images");
        signal.LeftWithoutCoreUseRate.Should().Be(70m, "listings are not a core feature");
        signal.LeaveGroups.Single(group => group.Key == PlanChurnSignals.NeverStarted).LeftRate.Should().Be(70m);
    }

    [TestMethod]
    public void Build_PutsEachCustomerWhoLeftInOneGroup()
    {
        // 5 left: one never used the core feature, one met failures, one kept hitting the daily cap,
        // one faded out and one used it steadily. 10 stayed, all steady.
        var stayed = Users(10);
        var left = Users(5);
        var (never, failing, capped, faded, steady) = (left[0], left[1], left[2], left[3], left[4]);
        var end = LeftOn;
        var uses = new List<SellerUse>();
        void Add(Guid userId, DateOnly day, int count) => uses.AddRange(Enumerable.Range(0, count).Select(_ => Use(userId, day)));

        foreach (var userId in stayed) Add(userId, To.AddDays(-5), 5);
        Add(failing, end.AddDays(-5), 5);
        // Three days at the cap of 4.
        foreach (var daysBefore in new[] { 3, 6, 9 }) Add(capped, end.AddDays(-daysBefore), 4);
        // 10 uses in the 30 days before their last 30, then 2: a fall of 80%.
        Add(faded, end.AddDays(-45), 10);
        Add(faded, end.AddDays(-10), 2);
        Add(steady, end.AddDays(-45), 4);
        Add(steady, end.AddDays(-10), 4);
        // 2 of 5 attempts failed: 40%, above the 20% rule.
        var attempts = Enumerable.Range(0, 5).Select(index => new SellerAttempt(failing, At(end.AddDays(-5)), index < 2)).ToList();

        var signal = Build(stayed, left, [new ChurnFeature("mockups", "Rendered mockups", true, uses, attempts, DailyCap: 4)]);

        signal.LeaveGroups.Select(group => (group.Key, group.Left)).Should().Equal(
            (PlanChurnSignals.NeverStarted, 1), (PlanChurnSignals.HitFailures, 1), (PlanChurnSignals.HitLimits, 1),
            (PlanChurnSignals.Faded, 1), (PlanChurnSignals.Steady, 1));
        signal.LeaveGroups.Sum(group => group.LeftRate).Should().Be(100m);
        var neverStarted = signal.LeaveGroups.First();
        (neverStarted.LeftRate, neverStarted.StayedRate).Should().Be(((decimal?)20m, (decimal?)0m));
        neverStarted.Notable.Should().BeFalse("one customer is too few to call a pattern");
        signal.LeaveGroups.Last().StayedRate.Should().Be(100m, "everyone who stayed used it steadily");
    }

    [TestMethod]
    public void Build_WithTooFewLeavers_StillComparesButFlagsASmallSample()
    {
        var left = Users(PlanUsageSignals.MinimumGroup - 1);
        var uses = left.Select(userId => Use(userId, new DateOnly(2026, 10, 1))).ToList();

        var signal = Build(Users(20), left, [Feature("design_images", uses)]);

        // Plan Insights shows small groups with a warning; the AI review leaves them out.
        signal.HasEnoughData.Should().BeFalse();
        signal.Left.Should().Be(PlanUsageSignals.MinimumGroup - 1);
        signal.LeftInFirstMonthRate.Should().NotBeNull();
        var feature = signal.Features.Single();
        feature.LeftRate.Should().Be(Math.Round(feature.LeftUsers * 100m / signal.Left, 1));
        feature.StayedRate.Should().Be(0m);
    }

    private static PlanChurnSignalDto Build(IReadOnlyList<Guid> stayed, IReadOnlyList<Guid> left, IReadOnlyList<ChurnFeature> features) =>
        PlanChurnSignals.Build(
            "pro",
            "Pro",
            new PlanCohortDto("Pro", stayed, left.Select(userId => new LeftCustomerDto(userId, new DateOnly(2026, 1, 1), LeftOn)).ToList()),
            From,
            To,
            features);

    private static ChurnFeature Feature(string metric, IReadOnlyList<SellerUse> uses, IReadOnlyList<SellerAttempt>? attempts = null) =>
        new(metric, metric, true, uses, attempts);

    private static SellerUse Use(Guid userId, DateOnly day) => new(userId, At(day));

    private static DateTime At(DateOnly day) => day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

    private static List<Guid> Users(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
}
