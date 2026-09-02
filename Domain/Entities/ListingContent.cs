using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents the generated marketplace listing content for a product.
/// </summary>
public sealed class ListingContent : AuditableEntity
{
    private readonly List<ListingTitle> _titles = [];
    private readonly List<ListingTag> _tags = [];
    private readonly List<ListingDescription> _descriptions = [];
    private readonly List<SeoScore> _seoScores = [];
    private readonly List<ListingGenerationHistory> _generationHistory = [];

    private ListingContent()
    {
    }

    /// <summary>
    /// Gets the target product identifier.
    /// </summary>
    public int ProductId { get; private set; }

    /// <summary>
    /// Gets the source batch job identifier, when applicable.
    /// </summary>
    public int? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the AI model used for generation.
    /// </summary>
    public string AiModelUsed { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the AI model version.
    /// </summary>
    public string ModelVersion { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the generation duration in seconds.
    /// </summary>
    public decimal GenerationTimeSeconds { get; private set; }

    /// <summary>
    /// Gets the generation API cost in USD.
    /// </summary>
    public decimal ApiCostUsd { get; private set; }

    /// <summary>
    /// Gets the content approval status.
    /// </summary>
    public string ApprovalStatus { get; private set; } = "pending";

    /// <summary>
    /// Gets the target product.
    /// </summary>
    public Product Product { get; private set; } = null!;

    /// <summary>
    /// Gets the source batch job, when applicable.
    /// </summary>
    public BatchJob? BatchJob { get; private set; }

    /// <summary>
    /// Gets generated title versions.
    /// </summary>
    public IReadOnlyCollection<ListingTitle> Titles => _titles.AsReadOnly();

    /// <summary>
    /// Gets generated tag versions.
    /// </summary>
    public IReadOnlyCollection<ListingTag> Tags => _tags.AsReadOnly();

    /// <summary>
    /// Gets generated description versions.
    /// </summary>
    public IReadOnlyCollection<ListingDescription> Descriptions => _descriptions.AsReadOnly();

    /// <summary>
    /// Gets calculated SEO scores.
    /// </summary>
    public IReadOnlyCollection<SeoScore> SeoScores => _seoScores.AsReadOnly();

    /// <summary>
    /// Gets listing generation history entries.
    /// </summary>
    public IReadOnlyCollection<ListingGenerationHistory> GenerationHistory => _generationHistory.AsReadOnly();
}
