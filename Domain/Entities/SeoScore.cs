using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents an SEO quality assessment for generated listing content.
/// </summary>
public sealed class SeoScore : BaseEntity, IHasModificationTime
{
    private SeoScore()
    {
    }

    /// <summary>
    /// Gets the parent listing content identifier.
    /// </summary>
    public int ListingContentId { get; private set; }

    /// <summary>
    /// Gets the target product identifier.
    /// </summary>
    public int ProductId { get; private set; }

    /// <summary>
    /// Gets the overall SEO score.
    /// </summary>
    public decimal OverallSeoScore { get; private set; }

    /// <summary>
    /// Gets the title SEO score.
    /// </summary>
    public decimal TitleScore { get; private set; }

    /// <summary>
    /// Gets the tags SEO score.
    /// </summary>
    public decimal TagsScore { get; private set; }

    /// <summary>
    /// Gets the description SEO score.
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
    /// Gets improvement suggestions as JSON.
    /// </summary>
    public string ImprovementSuggestions { get; private set; } = "[]";

    /// <summary>
    /// Gets the UTC score calculation timestamp.
    /// </summary>
    public DateTimeOffset CalculatedAtUtc { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Gets the parent listing content.
    /// </summary>
    public ListingContent ListingContent { get; private set; } = null!;
}
