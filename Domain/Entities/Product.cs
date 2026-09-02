using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a seller-owned product being processed by APCS.
/// </summary>
public sealed class Product : SoftDeletableEntity
{
    private readonly List<BatchJobProduct> _batchItems = [];
    private readonly List<AiPrompt> _aiPrompts = [];
    private readonly List<DesignImage> _designImages = [];
    private readonly List<MockupImage> _mockupImages = [];
    private readonly List<PromoVideo> _promoVideos = [];

    private Product()
    {
    }

    /// <summary>
    /// Gets the identifier of the seller that owns the product.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the source batch job identifier, when applicable.
    /// </summary>
    public int? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the product name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the product type.
    /// </summary>
    public string ProductType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the product niche category.
    /// </summary>
    public string? NicheCategory { get; private set; }

    /// <summary>
    /// Gets the original product description supplied for generation.
    /// </summary>
    public string InputDescription { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the current product processing status.
    /// </summary>
    public string ProcessingStatus { get; private set; } = "pending";

    /// <summary>
    /// Gets the source batch job, when applicable.
    /// </summary>
    public BatchJob? BatchJob { get; private set; }

    /// <summary>
    /// Gets the batch queue items associated with this product.
    /// </summary>
    public IReadOnlyCollection<BatchJobProduct> BatchItems => _batchItems.AsReadOnly();

    /// <summary>
    /// Gets the AI prompts generated for this product.
    /// </summary>
    public IReadOnlyCollection<AiPrompt> AiPrompts => _aiPrompts.AsReadOnly();

    /// <summary>
    /// Gets the generated design images.
    /// </summary>
    public IReadOnlyCollection<DesignImage> DesignImages => _designImages.AsReadOnly();

    /// <summary>
    /// Gets the generated mockup images.
    /// </summary>
    public IReadOnlyCollection<MockupImage> MockupImages => _mockupImages.AsReadOnly();

    /// <summary>
    /// Gets the generated promotional videos.
    /// </summary>
    public IReadOnlyCollection<PromoVideo> PromoVideos => _promoVideos.AsReadOnly();

    /// <summary>
    /// Gets the generated listing content, when available.
    /// </summary>
    public ListingContent? ListingContent { get; private set; }
}
