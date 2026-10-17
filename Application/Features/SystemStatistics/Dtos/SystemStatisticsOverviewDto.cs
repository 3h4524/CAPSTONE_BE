using System;
using System.Collections.Generic;

namespace APCS.Application.Features.SystemStatistics.Dtos;

public class SystemStatisticsOverviewDto
{
    public StatisticsMetricCardsDto Metrics { get; set; } = new();
    public List<DailyJobsPointDto> DailyJobs { get; set; } = new();
    public List<PipelineStageBreakdownDto> PipelineStages { get; set; } = new();
    public IntegrationStatusDto IntegrationStatus { get; set; } = new();
    public List<SystemErrorLogDto> RecentErrors { get; set; } = new();
}

public class MetricValueDto
{
    public long Value { get; set; }
    public long PreviousValue { get; set; }
    public decimal GrowthPercentage { get; set; }
}

public class StatisticsMetricCardsDto
{
    public MetricValueDto TotalBatchJobs { get; set; } = new();
    public MetricValueDto ImagesGenerated { get; set; } = new();
    public MetricValueDto VideosRendered { get; set; } = new();
    public MetricValueDto ListingsGenerated { get; set; } = new();
}

public class DailyJobsPointDto
{
    public string DateLabel { get; set; } = null!;
    public int Count { get; set; }
}

public class PipelineStageBreakdownDto
{
    public string StageName { get; set; } = null!;
    public int Count { get; set; }
    public decimal Percentage { get; set; }
}

public class IntegrationStatusDto
{
    public decimal EtsySuccessRate { get; set; }
    public int EtsyTotal { get; set; }
    public decimal EtsyGrowthPercentage { get; set; }
    public decimal PrintifySuccessRate { get; set; }
    public int PrintifyTotal { get; set; }
    public decimal PrintifyGrowthPercentage { get; set; }
    public decimal SocialMediaSuccessRate { get; set; }
    public int SocialMediaTotal { get; set; }
    public decimal SocialMediaGrowthPercentage { get; set; }
    public int FailedIntegrationsCount { get; set; }
}

public class SystemErrorLogDto
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string ErrorType { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? Source { get; set; }
}
