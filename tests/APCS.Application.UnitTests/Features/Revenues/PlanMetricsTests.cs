using System;
using System.Collections.Generic;
using System.Linq;
using APCS.Application.Features.Revenues;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Revenues;

[TestClass]
public sealed class PlanMetricsTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    private static SubscriptionRecord Live(string plan, Guid? userId = null, DateTime? cancelledAt = null, DateOnly? paidOn = null) =>
        new(Guid.NewGuid(), userId ?? Guid.NewGuid(), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            cancelledAt, paidOn ?? new DateOnly(2026, 9, 1), plan);

    [TestMethod]
    public void Arpu_WithPayingCustomers_DividesRevenueByCustomers()
    {
        PlanMetrics.Arpu(300m, 4).Should().Be(75m);
        PlanMetrics.Arpu(100m, 3).Should().Be(33.33m);
    }

    [TestMethod]
    public void Arpu_WithZeroCustomers_IsUndefined()
    {
        PlanMetrics.Arpu(0m, 0).Should().BeNull();
        PlanMetrics.Arpu(150m, 0).Should().BeNull();
    }

    [TestMethod]
    public void Retention_WhenNobodyWasOnThePlanAtTheStart_IsUndefined()
    {
        // The plan only gained customers during the period: there is no previous base to retain.
        var subs = new List<SubscriptionRecord>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc), null, new DateOnly(2026, 10, 5), "Pro")
        };

        var result = PlanMetrics.Retention(subs, "Pro", From, To);

        result.ActiveAtStart.Should().Be(0);
        result.Rate.Should().BeNull();
    }

    [TestMethod]
    public void Retention_BelowTheMinimumSample_ReportsCountsButNoRate()
    {
        var subs = Enumerable.Range(0, 4).Select(_ => Live("Pro")).ToList();

        var result = PlanMetrics.Retention(subs, "Pro", From, To);

        result.ActiveAtStart.Should().Be(4);
        result.Retained.Should().Be(4);
        result.Rate.Should().BeNull("4 customers is below the minimum sample of 5");
    }

    [TestMethod]
    public void Retention_CountsCancellationsAsLostAndPlanChangesAsRetained()
    {
        var switcher = Guid.NewGuid();
        var subs = Enumerable.Range(0, 4).Select(_ => Live("Pro")).ToList();
        subs.Add(Live("Pro", cancelledAt: new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc)));
        subs.Add(Live("Pro", userId: switcher, cancelledAt: new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc)));
        subs.Add(new SubscriptionRecord(Guid.NewGuid(), switcher, new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), null,
            new DateOnly(2026, 10, 12), "Studio"));

        var result = PlanMetrics.Retention(subs, "Pro", From, To);

        result.ActiveAtStart.Should().Be(6);
        result.Retained.Should().Be(5, "the switcher still pays, on another plan; only the canceller is lost");
        result.Rate.Should().Be(83.3m);
    }

    [TestMethod]
    public void Retention_IgnoresUnpaidCheckouts()
    {
        var subs = Enumerable.Range(0, 5).Select(_ => Live("Pro")).ToList();
        subs.Add(new SubscriptionRecord(Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), null, null, "Pro"));

        PlanMetrics.Retention(subs, "Pro", From, To).ActiveAtStart.Should().Be(5);
    }

    [TestMethod]
    public void Retention_WhenOnlyOnePlanHasData_LeavesTheOthersUndefined()
    {
        var subs = Enumerable.Range(0, 5).Select(_ => Live("Pro")).ToList();

        PlanMetrics.Retention(subs, "Pro", From, To).Rate.Should().Be(100m);
        var other = PlanMetrics.Retention(subs, "Basic", From, To);
        other.ActiveAtStart.Should().Be(0);
        other.Rate.Should().BeNull();
    }
}
