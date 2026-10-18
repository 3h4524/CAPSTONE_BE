using System;

namespace APCS.Application.Features.SystemStatistics.Dtos;

public class StatisticsFilterDto
{
    /// <summary>
    /// Time range options: "7d", "30d", "90d", "year". Defaults to "30d".
    /// </summary>
    public string TimeRange { get; set; } = "30d";

    /// <summary>
    /// Optional explicit start date (UTC). If specified, overrides TimeRange calculation.
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Optional explicit end date (UTC). If specified, overrides TimeRange calculation.
    /// </summary>
    public DateTime? EndDate { get; set; }
}
