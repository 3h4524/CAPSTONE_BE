using System.ComponentModel.DataAnnotations;
using APCS.Application.Abstractions.Commerce;

namespace APCS.Infrastructure.Options;

/// <summary>Controls optional scheduled public-market synchronization.</summary>
public sealed class MarketResearchOptions
{
    public const string SectionName = "MarketResearch:Scheduled";

    public bool Enabled { get; set; }

    public CommercePlatform Platform { get; set; } = CommercePlatform.Etsy;

    public string[] Queries { get; set; } = [];

    [Range(1, 100)]
    public int LimitPerQuery { get; set; } = 25;

    [Range(0, 100)]
    public double MinimumViralScore { get; set; } = 50;

    [Range(1, 25)]
    public int TopN { get; set; } = 3;

    [Range(15, 10080)]
    public int IntervalMinutes { get; set; } = 1440;
}
