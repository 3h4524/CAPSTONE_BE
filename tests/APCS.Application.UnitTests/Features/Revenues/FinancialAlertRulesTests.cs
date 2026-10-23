using System.Linq;
using APCS.Application.Features.Revenues;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace APCS.Application.UnitTests.Features.Revenues;

[TestClass]
public sealed class FinancialAlertRulesTests
{
    /// <summary>A healthy business: steady revenue, flat churn, nothing pending.</summary>
    private static readonly FinancialAlertInputs Healthy = new()
    {
        CurrentRevenue = 1000m,
        PreviousRevenue = 1000m,
        CurrentChurn = 5m,
        PreviousChurn = 5m,
        PaidInvoiceCount = 100,
        TopPlanName = "Creator",
        TopPlanRevenue = 400m,
        ForecastStatus = LinearRevenueForecast.StatusOk,
        ForecastWeeklyTrend = 10m,
        ForecastRSquared = 0.6m
    };

    private static string[] Ids(FinancialAlertInputs inputs) => FinancialAlertRules.Evaluate(inputs).Select(a => a.Id).ToArray();

    [TestMethod]
    public void Evaluate_WhenNothingIsWrong_ReportsOnlyAllClear()
    {
        Ids(Healthy).Should().Equal("all-clear");
    }

    [TestMethod]
    public void Evaluate_LapsedSubscriptions_AreADangerWithTheMonthlyValueAtRisk()
    {
        var alert = FinancialAlertRules.Evaluate(Healthy with { LapsedCount = 3, LapsedMonthlyValue = 89.97m, LapsedOldestDays = 12 }).First();

        alert.Id.Should().Be("lapsed-subscriptions");
        alert.Type.Should().Be("danger");
        alert.Amount.Should().Be(89.97m);
        alert.Count.Should().Be(3, "a client marks the alert new again when the count grows");
        alert.Description.Should().Contain("3 active subscriptions").And.Contain("12 days");
    }

    [TestMethod]
    public void Evaluate_RevenueDrop_FiresFromTenPercentAndNeverAgainstAnEmptyPreviousPeriod()
    {
        Ids(Healthy with { CurrentRevenue = 900m }).Should().Contain("revenue-drop");
        Ids(Healthy with { CurrentRevenue = 901m }).Should().NotContain("revenue-drop");
        Ids(Healthy with { CurrentRevenue = 0m, PreviousRevenue = 0m }).Should().NotContain("revenue-drop");
    }

    [TestMethod]
    public void Evaluate_ChurnRising_ComparesPercentagePointsAndNeedsBothRates()
    {
        Ids(Healthy with { CurrentChurn = 7m }).Should().Contain("churn-rising");
        FinancialAlertRules.Evaluate(Healthy with { CurrentChurn = 7m }).Single(a => a.Id == "churn-rising").Count.Should().BeNull("a rate has nothing to count");
        Ids(Healthy with { CurrentChurn = 6.9m }).Should().NotContain("churn-rising");
        Ids(Healthy with { CurrentChurn = 20m, PreviousChurn = null }).Should().NotContain("churn-rising");
    }

    [TestMethod]
    public void Evaluate_FailedPayments_FireOnTheShareOfAttemptsNotTheCount()
    {
        // 12 of 112 attempts = 10.7%; 5 of 105 = 4.8%.
        Ids(Healthy with { FailedPaymentCount = 12 }).Should().Contain("failed-payments");
        Ids(Healthy with { FailedPaymentCount = 5 }).Should().NotContain("failed-payments");

        FinancialAlertRules.Evaluate(Healthy with { FailedPaymentCount = 12 })
            .Single(a => a.Id == "failed-payments").ActionStatus.Should().Be("cancelled");
    }

    [TestMethod]
    public void Evaluate_StuckCheckouts_PointAtThePendingTransactions()
    {
        var alert = FinancialAlertRules.Evaluate(Healthy with { StuckCheckoutCount = 2, StuckCheckoutAmount = 59.98m })
            .Single(a => a.Id == "stuck-checkouts");

        alert.Type.Should().Be("warning");
        alert.ActionStatus.Should().Be("pending");
        alert.Amount.Should().Be(59.98m);
    }

    [TestMethod]
    public void Evaluate_Refunds_FireFromFivePercentOfRevenue()
    {
        Ids(Healthy with { RefundedAmount = 50m }).Should().Contain("refunds-high");
        Ids(Healthy with { RefundedAmount = 49m }).Should().NotContain("refunds-high");
    }

    [TestMethod]
    public void Evaluate_FallingForecast_FiresOnlyWhenTheTrendIsReliable()
    {
        Ids(Healthy with { ForecastWeeklyTrend = -20m }).Should().Contain("forecast-down");
        Ids(Healthy with { ForecastWeeklyTrend = -20m, ForecastRSquared = 0.1m }).Should().NotContain("forecast-down");
        Ids(Healthy with { ForecastWeeklyTrend = -20m, ForecastStatus = LinearRevenueForecast.StatusLowConfidence })
            .Should().NotContain("forecast-down");
    }

    [TestMethod]
    public void Evaluate_PlanConcentration_FiresFromSixtyPercent()
    {
        Ids(Healthy with { TopPlanRevenue = 600m }).Should().Contain("plan-concentration");
        Ids(Healthy with { TopPlanRevenue = 599m }).Should().NotContain("plan-concentration");
    }

    [TestMethod]
    public void Evaluate_InformationalAlerts_DoNotHideTheAllClear()
    {
        Ids(Healthy with { RenewalsDueCount = 4, RenewalsDueAmount = 120m, TopPlanRevenue = 700m })
            .Should().Equal("plan-concentration", "renewals-due", "all-clear");
    }

    [TestMethod]
    public void Evaluate_OrdersAlertsBySeverity()
    {
        var types = FinancialAlertRules.Evaluate(Healthy with
        {
            RenewalsDueCount = 1,
            StuckCheckoutCount = 1,
            LapsedCount = 1,
            DowngradeCount = 1,
            DowngradeMonthlyLoss = 20m
        }).Select(a => a.Type);

        types.Should().Equal("danger", "warning", "warning", "info");
    }
}
