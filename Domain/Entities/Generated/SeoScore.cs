using System;
using System.Collections.Generic;

namespace APCS.Domain.Entities;

public partial class SeoScore
{
    public Guid Id { get; set; }

    public Guid ListingContentId { get; set; }

    public decimal OverallSeoScore { get; set; }

    public decimal TitleScore { get; set; }

    public decimal TagsScore { get; set; }

    public decimal DescriptionScore { get; set; }

    public decimal KeywordOptimizationScore { get; set; }

    public decimal TagRelevanceScore { get; set; }

    public decimal KeywordDensity { get; set; }

    public string ImprovementSuggestions { get; set; } = null!;

    public string ScoringAlgorithmVersion { get; set; } = null!;

    public DateTime? CalculatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ListingContent ListingContent { get; set; } = null!;
}
