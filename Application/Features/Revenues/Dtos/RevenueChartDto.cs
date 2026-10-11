using System;
using System.Collections.Generic;

namespace APCS.Application.Features.Revenues.Dtos;

public class RevenueChartDto
{
    public List<RevenueTrendPointDto> Trend { get; set; } = new();
    public List<RevenueByPlanDto> ByPlan { get; set; } = new();
}

public class RevenueTrendPointDto
{
    public string DateLabel { get; set; } = null!;
    public decimal Amount { get; set; }
}

public class RevenueByPlanDto
{
    public string PlanName { get; set; } = null!;
    public decimal Amount { get; set; }
    public decimal Percentage { get; set; }
}
