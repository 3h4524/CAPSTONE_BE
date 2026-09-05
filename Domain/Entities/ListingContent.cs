using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents the marketplace listing copy generated for a product.
/// </summary>
public sealed class ListingContent : AuditableEntity
{
    private readonly List<ListingTitle> _titles = [];
    private readonly List<ListingTag> _tags = [];
    private readonly List<ListingDescription> _descriptions = [];
    private readonly List<ListingGenerationHistory> _generationHistory = [];

    private ListingContent()
    {
    }

    /// <summary>
    /// Gets the product this listing describes.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Gets the source batch job identifier, when applicable.
    /// </summary>
    public Guid? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the provider call that generated this listing, when recorded.
    /// </summary>
    public Guid? ApiUsageRecordId { get; private set; }

    /// <summary>
    /// Gets the model used to generate the listing.
    /// </summary>
    public string AiModelUsed { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the model version.
    /// </summary>
    public string ModelVersion { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the generation duration in seconds.
    /// </summary>
    public decimal GenerationTimeSeconds { get; private set; }

    /// <summary>
    /// Gets the approval status.
    /// </summary>
    public string ApprovalStatus { get; private set; } = "pending";

    /// <summary>
    /// Gets the UTC approval timestamp, required once approved.
    /// </summary>
    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    /// <summary>
    /// Gets the described product.
    /// </summary>
    public Product Product { get; private set; } = null!;

    /// <summary>
    /// Gets the source batch job, when applicable.
    /// </summary>
    public BatchJob? BatchJob { get; private set; }

    /// <summary>
    /// Gets the computed SEO score.
    /// </summary>
    public SeoScore? SeoScore { get; private set; }

    /// <summary>
    /// Gets the generated title versions.
    /// </summary>
    public IReadOnlyCollection<ListingTitle> Titles => _titles.AsReadOnly();

    /// <summary>
    /// Gets the generated tag set versions.
    /// </summary>
    public IReadOnlyCollection<ListingTag> Tags => _tags.AsReadOnly();

    /// <summary>
    /// Gets the generated description versions.
    /// </summary>
    public IReadOnlyCollection<ListingDescription> Descriptions => _descriptions.AsReadOnly();

    /// <summary>
    /// Gets the generation attempts recorded for this listing.
    /// </summary>
    public IReadOnlyCollection<ListingGenerationHistory> GenerationHistory => _generationHistory.AsReadOnly();
}
