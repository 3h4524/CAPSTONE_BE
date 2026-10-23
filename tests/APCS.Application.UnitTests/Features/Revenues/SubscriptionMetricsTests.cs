using System;
using System.Collections.Generic;
using System.Linq;
using APCS.Application.Features.Revenues;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Revenues;

[TestClass]
public sealed class SubscriptionMetricsTests
{
    private static readonly DateOnly PeriodStart = new(2026, 9, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 9, 30);

    private static SubscriptionRecord Sub(
        Guid userId,
        DateTime createdAt,
        DateTime? cancelledAt = null,
        DateOnly? firstPaidOn = null) =>
        new(Guid.NewGuid(), userId, createdAt, cancelledAt, firstPaidOn);

    private static DateTime At(int month, int day, int hour = 10) => new(2026, month, day, hour, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void CountChurned_IgnoresCheckoutsThatWereNeverPaid()
    {
        var user = Guid.NewGuid();
        var subs = Enumerable.Range(1, 5)
            .Select(d => Sub(user, At(9, d), cancelledAt: At(9, d, 11)))
            .ToList();

        SubscriptionMetrics.CountChurned(subs, PeriodStart, PeriodEnd).Should().Be(0);
        SubscriptionMetrics.CountNew(subs, PeriodStart, PeriodEnd).Should().Be(0);
    }

    [TestMethod]
    public void CountChurned_CountsAPaidSubscriptionCancelledWithoutReplacement()
    {
        var subs = new List<SubscriptionRecord>
        {
            Sub(Guid.NewGuid(), At(8, 1), cancelledAt: At(9, 15), firstPaidOn: new DateOnly(2026, 8, 1))
        };

        SubscriptionMetrics.CountChurned(subs, PeriodStart, PeriodEnd).Should().Be(1);
    }

    [TestMethod]
    public void PlanSwitchViaBuy_IsNeitherAChurnNorANewSignup()
    {
        // Buy cancels the current plan at checkout, the moment the new subscription is created.
        var user = Guid.NewGuid();
        var oldPlan = Sub(user, At(8, 1), cancelledAt: At(9, 10), firstPaidOn: new DateOnly(2026, 8, 1));
        var newPlan = Sub(user, At(9, 10), firstPaidOn: new DateOnly(2026, 9, 10));
        var subs = new List<SubscriptionRecord> { oldPlan, newPlan };

        SubscriptionMetrics.CountChurned(subs, PeriodStart, PeriodEnd).Should().Be(0);
        SubscriptionMetrics.CountNew(subs, PeriodStart, PeriodEnd).Should().Be(0);
    }

    [TestMethod]
    public void PlanSwitchViaUpgrade_IsNeitherAChurnNorANewSignup()
    {
        // Upgrade keeps the old plan live until the new one is paid, days later.
        var user = Guid.NewGuid();
        var oldPlan = Sub(user, At(8, 1), cancelledAt: At(9, 12), firstPaidOn: new DateOnly(2026, 8, 1));
        var newPlan = Sub(user, At(9, 10), firstPaidOn: new DateOnly(2026, 9, 12));
        var subs = new List<SubscriptionRecord> { oldPlan, newPlan };

        SubscriptionMetrics.CountChurned(subs, PeriodStart, PeriodEnd).Should().Be(0);
        SubscriptionMetrics.CountNew(subs, PeriodStart, PeriodEnd).Should().Be(0);
    }

    [TestMethod]
    public void ACustomerReturningAfterChurning_IsANewSignup()
    {
        var user = Guid.NewGuid();
        var first = Sub(user, At(7, 1), cancelledAt: At(8, 1), firstPaidOn: new DateOnly(2026, 7, 1));
        var comeback = Sub(user, At(9, 5), firstPaidOn: new DateOnly(2026, 9, 5));

        SubscriptionMetrics.CountNew([first, comeback], PeriodStart, PeriodEnd).Should().Be(1);
    }

    [TestMethod]
    public void CountActiveAt_CountsCustomersWithALivePaidSubscriptionOnce()
    {
        var switcher = Guid.NewGuid();
        var subs = new List<SubscriptionRecord>
        {
            Sub(Guid.NewGuid(), At(8, 1), firstPaidOn: new DateOnly(2026, 8, 1)),
            Sub(switcher, At(7, 1), cancelledAt: At(9, 3), firstPaidOn: new DateOnly(2026, 7, 1)),
            Sub(switcher, At(8, 31, 23), firstPaidOn: new DateOnly(2026, 8, 31)),
            Sub(Guid.NewGuid(), At(7, 1), cancelledAt: At(8, 20), firstPaidOn: new DateOnly(2026, 7, 1)),
            Sub(Guid.NewGuid(), At(8, 25)),
            Sub(Guid.NewGuid(), At(9, 2), firstPaidOn: new DateOnly(2026, 9, 2))
        };

        SubscriptionMetrics.CountActiveAt(subs, PeriodStart).Should().Be(2,
            "the switcher counts once, the August churner and the never-paid and September subscriptions do not");
    }

    [TestMethod]
    public void MonthlyChurnRate_ForOneMonth_DividesCancellationsByCustomersActiveAtItsStart()
    {
        var subs = new List<SubscriptionRecord>
        {
            Sub(Guid.NewGuid(), At(7, 1), firstPaidOn: new DateOnly(2026, 7, 1)),
            Sub(Guid.NewGuid(), At(7, 1), firstPaidOn: new DateOnly(2026, 7, 1)),
            Sub(Guid.NewGuid(), At(7, 1), cancelledAt: At(9, 10), firstPaidOn: new DateOnly(2026, 7, 1))
        };

        SubscriptionMetrics.MonthlyChurnRate(subs, PeriodStart, PeriodEnd).Should().Be(33.3m, "1 churned / 3 active on Sep 1");
    }

    [TestMethod]
    public void MonthlyChurnRate_OverSeveralMonths_StaysAMonthlyRateInsteadOfAddingUp()
    {
        // 10 customers active since June; one leaves in each of July, August and September, while 20
        // newcomers join on Jul 15 and 5 of them leave in August. Dividing all 8 cancellations by the
        // 10 customers active on Jul 1 would report 80%.
        var subs = Enumerable.Range(0, 10)
            .Select(i => Sub(Guid.NewGuid(), At(6, 1), cancelledAt: i < 3 ? At(7 + i, 10) : null, firstPaidOn: new DateOnly(2026, 6, 1)))
            .Concat(Enumerable.Range(0, 20)
                .Select(i => Sub(Guid.NewGuid(), At(7, 15), cancelledAt: i < 5 ? At(8, 20) : null, firstPaidOn: new DateOnly(2026, 7, 15))))
            .ToList();

        // Customer-months at risk: 10 (Jul 1) + 29 (Aug 1) + 23 (Sep 1) = 62; 8 / 62 = 12.9%.
        SubscriptionMetrics.MonthlyChurnRate(subs, new DateOnly(2026, 7, 1), PeriodEnd).Should().Be(12.9m);
    }

    [TestMethod]
    public void MonthlyChurnRate_WeighsAPartialMonthByTheShareItCovers()
    {
        var subs = Enumerable.Range(0, 10)
            .Select(i => Sub(Guid.NewGuid(), At(7, 1), cancelledAt: i == 0 ? At(9, 5) : null, firstPaidOn: new DateOnly(2026, 7, 1)))
            .ToList();

        // Sep 1-15 is half of September: 10 customers × 0.5 = 5 customer-months; 1 / 5 = 20%.
        SubscriptionMetrics.MonthlyChurnRate(subs, PeriodStart, new DateOnly(2026, 9, 15)).Should().Be(20m);
    }

    [TestMethod]
    public void MonthlyChurnRate_WhenNobodyWasActiveAtAnyMonthStart_IsUndefined()
    {
        var subs = new List<SubscriptionRecord>
        {
            Sub(Guid.NewGuid(), At(9, 2), cancelledAt: At(9, 20), firstPaidOn: new DateOnly(2026, 9, 2))
        };

        SubscriptionMetrics.MonthlyChurnRate(subs, PeriodStart, PeriodEnd).Should().BeNull();
        SubscriptionMetrics.MonthlyChurnRate([], PeriodStart, PeriodEnd).Should().BeNull();
    }

    [TestMethod]
    public void ProductionShapedData_ReportsOneChurnInsteadOf22()
    {
        // Mirrors the September data behind the 22 cancellations / 84.6% churn on the dashboard:
        // 19 abandoned checkouts by 4 users, 1 declined checkout, 1 paid subscription cancelled.
        var users = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var subs = Enumerable.Range(0, 19)
            .Select(i => Sub(users[i % 4], At(9, 1 + i), cancelledAt: At(9, 1 + i, 11)))
            .Append(Sub(Guid.NewGuid(), At(9, 20), cancelledAt: At(9, 20, 11)))
            .Append(Sub(Guid.NewGuid(), At(8, 10), cancelledAt: At(9, 25), firstPaidOn: new DateOnly(2026, 8, 10)))
            .Concat(Enumerable.Range(0, 4).Select(_ => Sub(Guid.NewGuid(), At(8, 5), firstPaidOn: new DateOnly(2026, 8, 5))))
            .ToList();

        int churned = SubscriptionMetrics.CountChurned(subs, PeriodStart, PeriodEnd);
        int activeAtStart = SubscriptionMetrics.CountActiveAt(subs, PeriodStart);

        churned.Should().Be(1);
        activeAtStart.Should().Be(5);
        SubscriptionMetrics.MonthlyChurnRate(subs, PeriodStart, PeriodEnd).Should().Be(20m);
    }

    [TestMethod]
    public void CountRenewals_ExcludesEachSubscriptionsFirstPayment()
    {
        var renewing = Guid.NewGuid();
        var brandNew = Guid.NewGuid();
        var invoices = new List<PaidInvoiceRecord>
        {
            new(Guid.NewGuid(), renewing, new DateOnly(2026, 8, 3)),
            new(Guid.NewGuid(), renewing, new DateOnly(2026, 9, 3)),
            new(Guid.NewGuid(), brandNew, new DateOnly(2026, 9, 10))
        };

        SubscriptionMetrics.CountRenewals(invoices, PeriodStart, PeriodEnd).Should().Be(1);
        SubscriptionMetrics.CountRenewals(invoices, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)).Should().Be(0,
            "August only holds the renewing subscription's first payment");
    }
}
