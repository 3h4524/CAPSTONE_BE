using System;
using System.Collections.Generic;
using System.Linq;
using APCS.Application.Features.Revenues.Dtos;

namespace APCS.Application.Features.Revenues;

/// <summary>Everything the financial alert rules read, computed by <see cref="RevenueService"/>.</summary>
/// <remarks>
/// "Now" figures describe the business today whatever period is selected; "period" figures follow
/// the dashboard's date filter and are compared against the equal-length period before it.
/// </remarks>
internal sealed record FinancialAlertInputs
{
    // Now
    public int LapsedCount { get; init; }
    public decimal LapsedMonthlyValue { get; init; }
    public int LapsedOldestDays { get; init; }
    public int StuckCheckoutCount { get; init; }
    public decimal StuckCheckoutAmount { get; init; }
    public int DowngradeCount { get; init; }
    public decimal DowngradeMonthlyLoss { get; init; }
    public int RenewalsDueCount { get; init; }
    public decimal RenewalsDueAmount { get; init; }

    // Selected period
    public decimal CurrentRevenue { get; init; }
    public decimal PreviousRevenue { get; init; }
    public decimal? CurrentChurn { get; init; }
    public decimal? PreviousChurn { get; init; }
    public int FailedPaymentCount { get; init; }
    public int PaidInvoiceCount { get; init; }
    public decimal RefundedAmount { get; init; }
    public string? TopPlanName { get; init; }
    public decimal TopPlanRevenue { get; init; }

    // Forecast (last 26 completed weeks)
    public string ForecastStatus { get; init; } = LinearRevenueForecast.StatusInsufficient;
    public decimal ForecastWeeklyTrend { get; init; }
    public decimal ForecastRSquared { get; init; }
}

/// <summary>
/// Turns revenue figures into the alerts shown on the dashboard. An alert appears only when its
/// condition holds, so an empty list means nothing needs attention (reported as one "all clear").
/// </summary>
internal static class FinancialAlertRules
{
    /// <summary>Revenue falling by at least this percentage vs. the previous period is flagged.</summary>
    public const decimal RevenueDropPercent = 10m;

    /// <summary>Monthly churn rising by at least this many percentage points is flagged.</summary>
    public const decimal ChurnRisePoints = 2m;

    /// <summary>Failed payments at or above this share of payment attempts are flagged.</summary>
    public const decimal FailedPaymentPercent = 10m;

    /// <summary>Refunds at or above this share of gross revenue are flagged.</summary>
    public const decimal RefundPercent = 5m;

    /// <summary>One plan earning at least this share of revenue is flagged.</summary>
    public const decimal ConcentrationPercent = 60m;

    /// <summary>A falling forecast is flagged only when the trend explains at least this much variance.</summary>
    public const decimal ForecastMinRSquared = 0.3m;

    /// <summary>A checkout still awaiting payment after this long is considered stuck.</summary>
    public static readonly TimeSpan StuckCheckoutAge = TimeSpan.FromHours(24);

    /// <summary>How far ahead upcoming renewals are listed.</summary>
    public const int RenewalWindowDays = 7;

    /// <summary>How far ahead scheduled downgrades are listed.</summary>
    public const int DowngradeWindowDays = 30;

    private const string ScopeNow = "Now";
    private const string ScopePeriod = "Selected period";

    private static readonly string[] SeverityOrder = ["danger", "warning", "purple", "info", "success"];

    public static List<FinancialAlertDto> Evaluate(FinancialAlertInputs inputs)
    {
        var alerts = new List<FinancialAlertDto>();

        // Nothing bills a renewal automatically and nothing expires an unpaid plan, so an active
        // subscription past its renewal date keeps its features without paying.
        if (inputs.LapsedCount > 0)
        {
            alerts.Add(new FinancialAlertDto
            {
                Id = "lapsed-subscriptions",
                Count = inputs.LapsedCount,
                Type = "danger",
                Title = "Unpaid renewals",
                Description = $"{Plural(inputs.LapsedCount, "active subscription")} passed the renewal date without a new payment (oldest {Plural(inputs.LapsedOldestDays, "day")} ago).",
                Amount = inputs.LapsedMonthlyValue,
                AmountLabel = "per month at risk",
                Scope = ScopeNow
            });
        }

        if (inputs.PreviousRevenue > 0
            && RevenueMath.PercentageChange(inputs.CurrentRevenue, inputs.PreviousRevenue) is { } change
            && change <= -RevenueDropPercent)
        {
            alerts.Add(new FinancialAlertDto
            {
                Id = "revenue-drop",
                Type = "danger",
                Title = "Revenue dropped",
                Description = $"Revenue is down {Math.Round(-change, 1)}% vs. the previous period.",
                Amount = inputs.PreviousRevenue - inputs.CurrentRevenue,
                AmountLabel = "less than before",
                Scope = ScopePeriod
            });
        }

        if (inputs.CurrentChurn is { } churn && inputs.PreviousChurn is { } previousChurn
            && churn - previousChurn >= ChurnRisePoints)
        {
            alerts.Add(new FinancialAlertDto
            {
                Id = "churn-rising",
                Type = "warning",
                Title = "Churn rising",
                Description = $"Monthly churn rose from {previousChurn}% to {churn}% (+{churn - previousChurn} pp).",
                Scope = ScopePeriod
            });
        }

        int attempts = inputs.FailedPaymentCount + inputs.PaidInvoiceCount;
        if (inputs.FailedPaymentCount > 0 && inputs.FailedPaymentCount * 100m / attempts >= FailedPaymentPercent)
        {
            alerts.Add(new FinancialAlertDto
            {
                Id = "failed-payments",
                Count = inputs.FailedPaymentCount,
                Type = "warning",
                Title = "Failed payments",
                Description = $"{Plural(inputs.FailedPaymentCount, "payment")} declined or expired unpaid — {Math.Round(inputs.FailedPaymentCount * 100m / attempts, 1)}% of attempts.",
                Scope = ScopePeriod,
                ActionStatus = "cancelled"
            });
        }

        // An "issued" invoice is a PayOS link awaiting confirmation; one older than a day usually
        // means the customer gave up or the payment webhook never arrived.
        if (inputs.StuckCheckoutCount > 0)
        {
            alerts.Add(new FinancialAlertDto
            {
                Id = "stuck-checkouts",
                Count = inputs.StuckCheckoutCount,
                Type = "warning",
                Title = "Unconfirmed checkouts",
                Description = $"{Plural(inputs.StuckCheckoutCount, "checkout")} still awaiting payment after {StuckCheckoutAge.TotalHours:0}h — check the PayOS webhook.",
                Amount = inputs.StuckCheckoutAmount,
                AmountLabel = "pending",
                Scope = ScopeNow,
                ActionStatus = "pending"
            });
        }

        if (inputs.CurrentRevenue > 0 && inputs.RefundedAmount * 100m / inputs.CurrentRevenue >= RefundPercent)
        {
            alerts.Add(new FinancialAlertDto
            {
                Id = "refunds-high",
                Type = "warning",
                Title = "High refunds",
                Description = $"Refunds equal {Math.Round(inputs.RefundedAmount * 100m / inputs.CurrentRevenue, 1)}% of revenue.",
                Amount = inputs.RefundedAmount,
                AmountLabel = "refunded",
                Scope = ScopePeriod,
                ActionStatus = "refunded"
            });
        }

        if (inputs.DowngradeCount > 0)
        {
            alerts.Add(new FinancialAlertDto
            {
                Id = "scheduled-downgrades",
                Count = inputs.DowngradeCount,
                Type = "warning",
                Title = "Scheduled downgrades",
                Description = $"{Plural(inputs.DowngradeCount, "customer")} will move to a cheaper plan in the next {DowngradeWindowDays} days.",
                Amount = inputs.DowngradeMonthlyLoss,
                AmountLabel = "less per month",
                Scope = ScopeNow
            });
        }

        if (inputs.ForecastStatus == LinearRevenueForecast.StatusOk
            && inputs.ForecastWeeklyTrend < 0
            && inputs.ForecastRSquared >= ForecastMinRSquared)
        {
            alerts.Add(new FinancialAlertDto
            {
                Id = "forecast-down",
                Type = "warning",
                Title = "Forecast trending down",
                Description = $"Weekly revenue is falling over the last 26 weeks (R² = {inputs.ForecastRSquared:0.00}).",
                Amount = -inputs.ForecastWeeklyTrend,
                AmountLabel = "less each week",
                Scope = "Last 26 weeks"
            });
        }

        if (inputs.CurrentRevenue > 0 && inputs.TopPlanName is not null
            && inputs.TopPlanRevenue * 100m / inputs.CurrentRevenue >= ConcentrationPercent)
        {
            alerts.Add(new FinancialAlertDto
            {
                Id = "plan-concentration",
                Type = "purple",
                Title = "Revenue depends on one plan",
                Description = $"{inputs.TopPlanName} earns {Math.Round(inputs.TopPlanRevenue * 100m / inputs.CurrentRevenue, 1)}% of revenue.",
                Scope = ScopePeriod
            });
        }

        if (inputs.RenewalsDueCount > 0)
        {
            alerts.Add(new FinancialAlertDto
            {
                Id = "renewals-due",
                Count = inputs.RenewalsDueCount,
                Type = "info",
                Title = "Renewals due",
                Description = $"{Plural(inputs.RenewalsDueCount, "subscription")} renew in the next {RenewalWindowDays} days.",
                Amount = inputs.RenewalsDueAmount,
                AmountLabel = "expected",
                Scope = $"Next {RenewalWindowDays} days"
            });
        }

        if (!alerts.Any(a => a.Type is "danger" or "warning"))
        {
            alerts.Add(new FinancialAlertDto
            {
                Id = "all-clear",
                Type = "success",
                Title = "All clear",
                Description = "No payment, churn or revenue issues need attention.",
                Scope = ScopeNow
            });
        }

        return alerts
            .OrderBy(a => Array.IndexOf(SeverityOrder, a.Type))
            .ToList();
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
