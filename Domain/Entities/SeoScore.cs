using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Holds the computed SEO assessment for a listing.
/// </summary>
/// <remarks>
/// One score per listing. The product is reached through the listing rather than stored again
/// here, so the two can never disagree.
/// </remarks>
public sealed class SeoScore : BaseEntity, IHasModificationTime
{
    private SeoScore()
    {
    }

    /// <summary>
    /// Gets the scored listing content identifier.
    /// </summary>
    public Guid ListingContentId { get; private set; }

    /// <summary>
    /// Gets the overall score, from zero to one hundred.
    /// </summary>
    public decimal OverallSeoScore { get; private set; }

    /// <summary>
    /// Gets the title component score.
    /// </summary>
    public decimal TitleScore { get; private set; }

    /// <summary>
    /// Gets the tag component score.
    /// </summary>
    public decimal TagsScore { get; private set; }

    /// <summary>
    /// Gets the description component score.
    /// </summary>
    public decimal DescriptionScore { get; private set; }

    /// <summary>
    /// Gets the keyword optimization score.
    /// </summary>
    public decimal KeywordOptimizationScore { get; private set; }

    /// <summary>
    /// Gets the tag relevance score.
    /// </summary>
    public decimal TagRelevanceScore { get; private set; }

    /// <summary>
    /// Gets the measured keyword density.
    /// </summary>
    public decimal KeywordDensity { get; private set; }

    /// <summary>
    /// Gets the improvement suggestions as JSON.
    /// </summary>
    public string ImprovementSuggestions { get; private set; } = "[]";

    /// <summary>
    /// Gets the version of the scoring algorithm that produced these numbers.
    /// </summary>
    public string ScoringAlgorithmVersion { get; private set; } = "v1";

    /// <summary>
    /// Gets the UTC timestamp when the score was calculated.
    /// </summary>
    public DateTimeOffset CalculatedAtUtc { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Gets the scored listing content.
    /// </summary>
    public ListingContent ListingContent { get; private set; } = null!;
}
