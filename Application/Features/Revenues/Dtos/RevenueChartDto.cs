using System;
using System.Collections.Generic;

namespace APCS.Application.Features.Revenues.Dtos;

public class RevenueChartDto
{
    public List<RevenueTrendPointDto> Trend { get; set; } = new();
    public List<RevenueByPlanDto> ByPlan { get; set; } = new();
    public List<string> AvailablePlans { get; set; } = new();
}

public class RevenueTrendPointDto
{
    public string DateLabel { get; set; } = null!;
    public decimal Amount { get; set; }
    public decimal PreviousAmount { get; set; }
    public Dictionary<string, decimal> PlanBreakdown { get; set; } = new();
}

public class RevenueByPlanDto
{
    public string PlanName { get; set; } = null!;
    public decimal Amount { get; set; }
    public decimal Percentage { get; set; }
    /// <summary>Number of paid invoices for the plan in the period.</summary>
    public int PurchaseCount { get; set; }
    /// <summary>Distinct customers who paid for the plan in the period.</summary>
    public int UniqueCustomers { get; set; }
    /// <summary>Amount / UniqueCustomers; null when nobody paid.</summary>
    public decimal? Arpu { get; set; }
    /// <summary>Customers live on the plan at the period start (the retention denominator).</summary>
    public int RetentionBase { get; set; }
    /// <summary>Percent of RetentionBase still paying after the period; null when RetentionBase is below 5.</summary>
    public decimal? RetentionRate { get; set; }
    /// <summary>The plan's collected revenue in the equally long previous period.</summary>
    public decimal PreviousAmount { get; set; }
    /// <summary>Change versus <see cref="PreviousAmount"/>; null when the plan earned nothing then.</summary>
    public decimal? PercentageChange { get; set; }
}
