using System;

namespace APCS.Application.Features.Revenues.Dtos;

public class RevenueMetricsDto
{
    public MetricDetail TotalNetRevenue { get; set; } = new();
    public MetricDetail SubscriptionMrr { get; set; } = new();
    public MetricDetail ActiveSubscribers { get; set; } = new();
    public MetricDetail Arpu { get; set; } = new();
}

public class MetricDetail
{
    public decimal Value { get; set; }
    public decimal PreviousValue { get; set; }
    public decimal PercentageChange { get; set; }
}
