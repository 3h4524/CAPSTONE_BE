using System;
using System.Collections.Generic;

namespace APCS.Application.Features.Revenues.Dtos;

public class RevenueMetricsDto
{
    public MetricDetail TotalRevenue { get; set; } = new();
    public MetricDetail NetRevenue { get; set; } = new();
    public MetricDetail SubscriptionMrr { get; set; } = new();
    public MetricDetail Arr { get; set; } = new();
    public MetricDetail ActiveSubscribers { get; set; } = new();
    public MetricDetail RefundAmount { get; set; } = new();

    // Backward compatibility aliases
    public MetricDetail TotalNetRevenue
    {
        get => NetRevenue;
        set => NetRevenue = value;
    }
    public MetricDetail Arpu { get; set; } = new();
}

public class MetricDetail
{
    public decimal Value { get; set; }
    public decimal PreviousValue { get; set; }
    public decimal PercentageChange { get; set; }
    public List<decimal> Sparkline { get; set; } = new();
}
