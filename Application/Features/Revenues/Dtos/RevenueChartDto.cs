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
    /// <summary>The plan's paying customers at the period's start and end, and what happened between.</summary>
    public PlanCustomerFlowDto Customers { get; set; } = new();
    /// <summary>
    /// Percent of <see cref="PlanCustomerFlowDto.Start"/> still paying at the end, on any plan; null
    /// when fewer than 5 customers were on the plan at the start.
    /// </summary>
    public decimal? RetentionRate { get; set; }
    /// <summary>The plan's collected revenue in the equally long previous period.</summary>
    public decimal PreviousAmount { get; set; }
    /// <summary>Change versus <see cref="PreviousAmount"/>; null when the plan earned nothing then.</summary>
    public decimal? PercentageChange { get; set; }
    /// <summary>The plan's MRR at the end of the day before the period.</summary>
    public decimal MrrStart { get; set; }
    /// <summary>The plan's MRR at the end of the period.</summary>
    public decimal MrrEnd { get; set; }
    /// <summary>Change of MRR versus <see cref="MrrStart"/> in percent; null when the plan had no MRR then.</summary>
    public decimal? MrrChangePercent { get; set; }
    /// <summary>What the customers who left the plan used least compared with those who stayed.</summary>
    public PlanUsageSignalDto UsageSignal { get; set; } = new();
}

/// <summary>
/// A plan's paying customers at the end of the day before the period and at the end of it, counted
/// like MRR. <c>Start = Stayed + SwitchedOut + StoppedPaying</c> and
/// <c>End = Stayed + New + SwitchedIn</c>.
/// </summary>
public class PlanCustomerFlowDto
{
    public int Start { get; set; }
    /// <summary>On the plan at both ends.</summary>
    public int Stayed { get; set; }
    /// <summary>Moved to another plan and still paying.</summary>
    public int SwitchedOut { get; set; }
    /// <summary>Paying for nothing at the end.</summary>
    public int StoppedPaying { get; set; }
    /// <summary>Paying for nothing at the start.</summary>
    public int New { get; set; }
    /// <summary>Moved here from another plan.</summary>
    public int SwitchedIn { get; set; }
    public int End { get; set; }
}

/// <summary>The feature with the largest usage gap between a plan's customers who stayed and who left.</summary>
public class PlanUsageSignalDto
{
    /// <summary>Customers on the plan at the start who were still on it at the end.</summary>
    public int Stayed { get; set; }
    /// <summary>Customers on the plan at the start who stopped paying by the end.</summary>
    public int Left { get; set; }
    /// <summary>Each group's minimum size for a comparison.</summary>
    public int MinimumGroup { get; set; }
    /// <summary>Whether both groups were large enough and some feature was used more by those who stayed.</summary>
    public bool HasSignal { get; set; }
    public string? Feature { get; set; }
    public string? FeatureLabel { get; set; }
    /// <summary>Share of the customers who stayed that used the feature, 0–100.</summary>
    public decimal? StayedRate { get; set; }
    /// <summary>Share of the customers who left that used the feature, 0–100.</summary>
    public decimal? LeftRate { get; set; }
}
