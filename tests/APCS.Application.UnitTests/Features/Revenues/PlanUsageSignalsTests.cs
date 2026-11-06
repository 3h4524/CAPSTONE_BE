using System;
using System.Collections.Generic;
using System.Linq;
using APCS.Application.Features.Revenues;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Revenues;

[TestClass]
public sealed class PlanUsageSignalsTests
{
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);

    [TestMethod]
    public void Groups_SplitsThePlansCustomersIntoStayedAndLeftAndLeavesOutPlanChanges()
    {
        var stayed = Guid.NewGuid();
        var left = Guid.NewGuid();
        var moved = Guid.NewGuid();
        var subscriptions = new List<MrrSubscription>
        {
            Pro(stayed),
            Pro(left, ended: new DateOnly(2026, 9, 10)),
            Pro(moved, ended: new DateOnly(2026, 9, 12)),
            new(moved, new DateOnly(2026, 9, 12), null, new DateOnly(2027, 1, 1), [new MrrSegment(new DateOnly(2026, 9, 12), "Studio", 999_000m)])
        };

        var (stayedGroup, leftGroup) = PlanUsageSignals.Groups(subscriptions, Sep1, Sep30, "Pro");

        stayedGroup.Should().BeEquivalentTo([stayed]);
        leftGroup.Should().BeEquivalentTo([left]);
    }

    [TestMethod]
    public void LargestGap_PicksTheFeatureTheStayersUsedMostMoreThanTheLeavers()
    {
        var stayed = Users(20);
        var left = Users(10);
        // Lifestyle: 15 of 20 stayers (75%) vs 1 of 10 leavers (10%). Video: 10 of 20 (50%) vs 4 of 10 (40%).
        var usage = new List<(string, string, IReadOnlySet<Guid>)>
        {
            ("lifestyle_mockups", "Lifestyle mockups", stayed.Take(15).Concat(left.Take(1)).ToHashSet()),
            ("ai_videos", "AI videos", stayed.Take(10).Concat(left.Take(4)).ToHashSet())
        };

        var gap = PlanUsageSignals.LargestGap(stayed, left, usage);

        gap.Should().NotBeNull();
        gap!.Feature.Should().Be("lifestyle_mockups");
        gap.StayedRate.Should().Be(75m);
        gap.LeftRate.Should().Be(10m);
    }

    [TestMethod]
    public void LargestGap_WithTooFewLeavers_ReturnsNull()
    {
        var usage = new List<(string, string, IReadOnlySet<Guid>)> { ("x", "X", new HashSet<Guid>()) };

        PlanUsageSignals.LargestGap(Users(20), Users(PlanUsageSignals.MinimumGroup - 1), usage).Should().BeNull();
    }

    [TestMethod]
    public void LargestGap_WhenLeaversUsedEverythingAsMuch_ReturnsNull()
    {
        var stayed = Users(10);
        var left = Users(10);
        var everyone = stayed.Concat(left).ToHashSet();

        PlanUsageSignals.LargestGap(stayed, left, [("x", "X", everyone)]).Should().BeNull();
    }

    [TestMethod]
    public void Tenure_ForACustomerWhoLeft_GivesTheirFirstPaymentAndTheDayTheyStoppedEarning()
    {
        var userId = Guid.NewGuid();
        // Cancelled on 10 Sep, before its 1 Jan renewal; an older one ran out unpaid on 1 Aug.
        var subscriptions = new List<MrrSubscription>
        {
            Pro(userId, ended: new DateOnly(2026, 9, 10)),
            new(userId, new DateOnly(2026, 6, 1), null, new DateOnly(2026, 8, 1), [new MrrSegment(new DateOnly(2026, 6, 1), "Starter", 249_750m)])
        };

        var (firstPaidOn, leftOn) = PlanUsageSignals.Tenure(subscriptions, userId, Sep30);

        firstPaidOn.Should().Be(new DateOnly(2026, 6, 1));
        leftOn.Should().Be(new DateOnly(2026, 9, 10));
    }

    [TestMethod]
    public void Tenure_ForACustomerStillPaying_HasNoLeavingDay()
    {
        var userId = Guid.NewGuid();

        PlanUsageSignals.Tenure([Pro(userId)], userId, Sep30).LeftOn.Should().BeNull();
    }

    private static HashSet<Guid> Users(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToHashSet();

    private static MrrSubscription Pro(Guid userId, DateOnly? ended = null) =>
        new(userId, new DateOnly(2026, 8, 1), ended, new DateOnly(2027, 1, 1), [new MrrSegment(new DateOnly(2026, 8, 1), "Pro", 499_000m)]);
}
