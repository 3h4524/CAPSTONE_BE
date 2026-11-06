using System;
using System.Collections.Generic;
using APCS.Application.Features.Revenues;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Revenues;

[TestClass]
public sealed class MrrLedgerTests
{
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);
    private static readonly DateOnly FarFuture = new(2027, 1, 1);

    [TestMethod]
    public void ValueAt_CountsASubscriptionFromItsFirstPaymentUntilItEnds()
    {
        var subscription = Monthly("Pro", 499_000m, firstPaid: new DateOnly(2026, 9, 10), ended: new DateOnly(2026, 9, 20));

        MrrLedger.TotalAt([subscription], new DateOnly(2026, 9, 9)).Should().Be(0m);
        MrrLedger.TotalAt([subscription], new DateOnly(2026, 9, 10)).Should().Be(499_000m, "it paid that day");
        MrrLedger.TotalAt([subscription], new DateOnly(2026, 9, 19)).Should().Be(499_000m);
        MrrLedger.TotalAt([subscription], new DateOnly(2026, 9, 20)).Should().Be(0m, "it ended that day");
    }

    [TestMethod]
    public void ValueAt_StopsCountingOnTheRenewalDateWhenUnpaid()
    {
        var subscription = Monthly("Pro", 499_000m, firstPaid: Sep1, renewal: new DateOnly(2026, 10, 1));

        MrrLedger.TotalAt([subscription], Sep30).Should().Be(499_000m);
        MrrLedger.TotalAt([subscription], new DateOnly(2026, 10, 1)).Should().Be(0m, "it is past due from its renewal date");
    }

    [TestMethod]
    public void ValueAt_UsesThePlanOfTheSegmentInEffect()
    {
        var userId = Guid.NewGuid();
        var subscription = new MrrSubscription(userId, Sep1, null, FarFuture,
        [
            new MrrSegment(Sep1, "Pro", 499_000m),
            new MrrSegment(new DateOnly(2026, 10, 1), "Starter", 199_000m)
        ]);

        MrrLedger.ValueAt([subscription], Sep30).Should().ContainKey((userId, "Pro")).WhoseValue.Should().Be(499_000m);
        MrrLedger.ValueAt([subscription], new DateOnly(2026, 10, 1)).Should().ContainKey((userId, "Starter")).WhoseValue.Should().Be(199_000m);
    }

    [TestMethod]
    public void CustomersAt_CountsACustomerHoldingTwoPlansOnce()
    {
        var userId = Guid.NewGuid();
        var subscriptions = new List<MrrSubscription>
        {
            Monthly("Starter", 199_000m, firstPaid: Sep1, userId: userId),
            Monthly("Pro", 499_000m, firstPaid: Sep1, userId: userId)
        };

        MrrLedger.CustomersAt(subscriptions, Sep30).Should().Be(1);
        MrrLedger.TotalAt(subscriptions, Sep30).Should().Be(698_000m);
    }

    [TestMethod]
    public void Movement_ForAllPlans_SplitsTheChangeAndAddsUpToTheEnd()
    {
        var upgrader = Guid.NewGuid();
        var downgrader = Guid.NewGuid();
        var subscriptions = new List<MrrSubscription>
        {
            // Stays on Starter.
            Monthly("Starter", 199_000m, firstPaid: new DateOnly(2026, 8, 1)),
            // New in September.
            Monthly("Pro", 499_000m, firstPaid: new DateOnly(2026, 9, 12)),
            // Leaves in September.
            Monthly("Pro", 499_000m, firstPaid: new DateOnly(2026, 8, 1), ended: new DateOnly(2026, 9, 5)),
            // Upgrades: the Starter subscription ends and a Pro one starts.
            Monthly("Starter", 199_000m, firstPaid: new DateOnly(2026, 8, 1), ended: new DateOnly(2026, 9, 15), userId: upgrader),
            Monthly("Pro", 499_000m, firstPaid: new DateOnly(2026, 9, 15), userId: upgrader),
            // Downgrades in place at a renewal.
            new MrrSubscription(downgrader, new DateOnly(2026, 8, 1), null, FarFuture,
            [
                new MrrSegment(new DateOnly(2026, 8, 1), "Pro", 499_000m),
                new MrrSegment(new DateOnly(2026, 9, 1), "Starter", 199_000m)
            ])
        };

        var movement = MrrLedger.Movement(subscriptions, Sep1, Sep30);

        // Start on Aug 31: 199 + 499 + 199 + 499 = 1,396k.
        movement.Start.Should().Be(1_396_000m);
        movement.New.Should().Be(499_000m);
        movement.Expansion.Should().Be(300_000m);
        movement.Contraction.Should().Be(300_000m);
        movement.Churn.Should().Be(499_000m);
        movement.MovedIn.Should().Be(0m);
        movement.MovedOut.Should().Be(0m);
        movement.End.Should().Be(1_396_000m + 499_000m + 300_000m - 300_000m - 499_000m);
        movement.End.Should().Be(MrrLedger.TotalAt(subscriptions, Sep30));
    }

    [TestMethod]
    public void Movement_ForOnePlan_CountsPlanChangesAsMovedInAndOut()
    {
        var upgrader = Guid.NewGuid();
        var subscriptions = new List<MrrSubscription>
        {
            Monthly("Starter", 199_000m, firstPaid: new DateOnly(2026, 8, 1), ended: new DateOnly(2026, 9, 15), userId: upgrader),
            Monthly("Pro", 499_000m, firstPaid: new DateOnly(2026, 9, 15), userId: upgrader),
            Monthly("Starter", 199_000m, firstPaid: new DateOnly(2026, 8, 1), ended: new DateOnly(2026, 9, 3))
        };

        var starter = MrrLedger.Movement(subscriptions, Sep1, Sep30, "Starter");
        var pro = MrrLedger.Movement(subscriptions, Sep1, Sep30, "Pro");

        starter.Start.Should().Be(398_000m);
        starter.MovedOut.Should().Be(199_000m, "the upgrader still pays, on Pro");
        starter.Churn.Should().Be(199_000m);
        starter.End.Should().Be(0m);
        pro.Start.Should().Be(0m);
        pro.MovedIn.Should().Be(499_000m);
        pro.New.Should().Be(0m, "the customer was already paying");
        pro.End.Should().Be(499_000m);
    }

    [TestMethod]
    public void CustomerFlow_SplitsTheStartAndTheEndAndAddsUp()
    {
        // October on Starter: of 4 customers at the start, 1 stays, 1 moves to Pro and 2 stop paying.
        // 2 new customers join, and 1 Pro customer moves down to Starter.
        var oct1 = new DateOnly(2026, 10, 1);
        var oct31 = new DateOnly(2026, 10, 31);
        var subscriptions = new List<MrrSubscription>
        {
            Monthly("Starter", 199_000m, firstPaid: Sep1),
            new(Guid.NewGuid(), Sep1, null, FarFuture,
                [new MrrSegment(Sep1, "Starter", 199_000m), new MrrSegment(new DateOnly(2026, 10, 10), "Pro", 499_000m)]),
            Monthly("Starter", 199_000m, firstPaid: Sep1, ended: new DateOnly(2026, 10, 5)),
            Monthly("Starter", 199_000m, firstPaid: Sep1, renewal: new DateOnly(2026, 10, 15)),
            Monthly("Starter", 199_000m, firstPaid: new DateOnly(2026, 10, 3)),
            Monthly("Starter", 199_000m, firstPaid: new DateOnly(2026, 10, 20)),
            new(Guid.NewGuid(), Sep1, null, FarFuture,
                [new MrrSegment(Sep1, "Pro", 499_000m), new MrrSegment(new DateOnly(2026, 10, 12), "Starter", 199_000m)]),
        };

        var flow = MrrLedger.CustomerFlow(subscriptions, oct1, oct31, "Starter");

        flow.Should().Be(new MrrCustomerFlow(Start: 4, Stayed: 1, SwitchedOut: 1, StoppedPaying: 2, New: 2, SwitchedIn: 1, End: 4));
        flow.Retained.Should().Be(2, "the customer who moved to Pro still pays");
        (flow.Stayed + flow.SwitchedOut + flow.StoppedPaying).Should().Be(flow.Start);
        (flow.Stayed + flow.New + flow.SwitchedIn).Should().Be(flow.End);
    }

    private static MrrSubscription Monthly(
        string plan,
        decimal value,
        DateOnly firstPaid,
        DateOnly? ended = null,
        DateOnly? renewal = null,
        Guid? userId = null) =>
        new(userId ?? Guid.NewGuid(), firstPaid, ended, renewal ?? FarFuture, [new MrrSegment(firstPaid, plan, value)]);
}
