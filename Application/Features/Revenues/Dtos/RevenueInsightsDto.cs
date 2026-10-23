using System;
using System.Collections.Generic;

namespace APCS.Application.Features.Revenues.Dtos;

public class RevenueInsightsDto
{
    public SubscriptionAnalyticsDto SubscriptionAnalytics { get; set; } = new();
    public RevenueForecastDto Forecast { get; set; } = new();
    public List<FinancialAlertDto> FinancialAlerts { get; set; } = new();
}

public class SubscriptionAnalyticsDto
{
    public List<SubscriptionAnalyticsPointDto> Points { get; set; } = new();
    public MetricDetail NewSubscriptions { get; set; } = new();
    public MetricDetail Renewals { get; set; } = new();
    public MetricDetail Cancellations { get; set; } = new();
    /// <summary>
    /// Average monthly churn in percent over the period; 0 with a null change when undefined. Its
    /// change is in percentage points, not a relative percentage.
    /// </summary>
    public MetricDetail ChurnRate { get; set; } = new();
    /// <summary>Whether the churn rate is defined, i.e. someone was active at the start of a month in the period.</summary>
    public bool HasChurnRate { get; set; }
    /// <summary>Customers with a live paid subscription when the period began.</summary>
    public int ActiveAtPeriodStart { get; set; }
}

public class SubscriptionAnalyticsPointDto
{
    public string DateLabel { get; set; } = null!;
    public int NewSubscriptions { get; set; }
    public int Renewals { get; set; }
    public int Cancellations { get; set; }
}

public class RevenueForecastDto
{
    public List<ForecastPointDto> Points { get; set; } = new();
    public decimal CurrentMrr { get; set; }
    /// <summary>The fitted weekly trend as a percentage of the mean weekly revenue.</summary>
    public decimal ProjectedGrowthRate { get; set; }
    /// <summary>"ok", "low_confidence" (fewer than 14 weeks) or "insufficient" (fewer than 8 weeks; no forecast points).</summary>
    public string Status { get; set; } = "insufficient";
    /// <summary>Number of completed weeks the regression was fitted on.</summary>
    public int SampleWeeks { get; set; }
    public decimal RSquared { get; set; }
    /// <summary>The regression slope: revenue change per week.</summary>
    public decimal WeeklyTrend { get; set; }
}

public class ForecastPointDto
{
    /// <summary>The week label (week start, e.g. "Oct 13"); the name is kept for API compatibility.</summary>
    public string MonthLabel { get; set; } = null!;
    public bool IsHistorical { get; set; }
    public decimal? Actual { get; set; }
    public decimal? ForecastBase { get; set; }
    public decimal? Optimistic { get; set; }
    public decimal? Conservative { get; set; }
    public decimal PercentageChangeVsCurrent { get; set; }
}

public class FinancialAlertDto
{
    /// <summary>The rule that raised the alert, e.g. "lapsed-subscriptions"; stable across requests.</summary>
    public string Id { get; set; } = null!;
    public string Type { get; set; } = "info"; // "danger", "warning", "info", "purple", "success"
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    /// <summary>
    /// How many things the alert counts (subscriptions, payments...), or null for a rate alert. A
    /// client can treat a higher count as a new alert even when it has seen this rule before.
    /// </summary>
    public int? Count { get; set; }
    /// <summary>A money figure for the alert, in the same unit as invoice amounts; the client formats it.</summary>
    public decimal? Amount { get; set; }
    /// <summary>What <see cref="Amount"/> means, e.g. "per month at risk".</summary>
    public string? AmountLabel { get; set; }
    /// <summary>What the alert describes: "Now", "Selected period", "Next 7 days" or "Last 26 weeks".</summary>
    public string Scope { get; set; } = null!;
    /// <summary>A transaction status ("pending", "cancelled", "refunded") the client can filter the table by.</summary>
    public string? ActionStatus { get; set; }
}
