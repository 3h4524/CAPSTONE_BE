using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a print-on-demand product being generated for a user.
/// </summary>
public sealed class Product : SoftDeletableEntity
{
    private readonly List<AiPrompt> _prompts = [];
    private readonly List<DesignImage> _designImages = [];
    private readonly List<MockupImage> _mockupImages = [];
    private readonly List<ProductMockupTemplate> _mockupTemplates = [];
    private readonly List<PromoVideo> _promoVideos = [];

    private Product()
    {
    }

    /// <summary>
    /// Gets the owning user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the source design template identifier, when one was chosen.
    /// </summary>
    public Guid? DesignTemplateId { get; private set; }

    /// <summary>
    /// Gets the product name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the product type.
    /// </summary>
    public string ProductType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the target niche category.
    /// </summary>
    public string? NicheCategory { get; private set; }

    /// <summary>
    /// Gets the target audience.
    /// </summary>
    public string? TargetAudience { get; private set; }

    /// <summary>
    /// Gets the seller-provided product description used to drive generation.
    /// </summary>
    public string InputDescription { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the requested on-design text.
    /// </summary>
    public string? DesiredDesignText { get; private set; }

    /// <summary>
    /// Gets the requested style preset.
    /// </summary>
    public string? StylePreset { get; private set; }

    /// <summary>
    /// Gets the primary keywords.
    /// </summary>
    public string? MainKeywords { get; private set; }

    /// <summary>
    /// Gets the colour preference.
    /// </summary>
    public string? ColorPreference { get; private set; }

    /// <summary>
    /// Gets free-form seller notes.
    /// </summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// Gets the pipeline processing status.
    /// </summary>
    public string ProcessingStatus { get; private set; } = "pending";

    /// <summary>
    /// Gets the source design template, when one was chosen.
    /// </summary>
    public DesignTemplate? DesignTemplate { get; private set; }

    /// <summary>
    /// Gets the listing content generated for this product.
    /// </summary>
    public ListingContent? ListingContent { get; private set; }

    /// <summary>
    /// Gets the prompts generated for this product.
    /// </summary>
    public IReadOnlyCollection<AiPrompt> Prompts => _prompts.AsReadOnly();

    /// <summary>
    /// Gets the design images generated for this product.
    /// </summary>
    public IReadOnlyCollection<DesignImage> DesignImages => _designImages.AsReadOnly();

    /// <summary>
    /// Gets the mockups rendered for this product.
    /// </summary>
    public IReadOnlyCollection<MockupImage> MockupImages => _mockupImages.AsReadOnly();

    /// <summary>
    /// Gets the mockup templates selected for this product.
    /// </summary>
    public IReadOnlyCollection<ProductMockupTemplate> MockupTemplates => _mockupTemplates.AsReadOnly();

    /// <summary>
    /// Gets the promotional videos rendered for this product.
    /// </summary>
    public IReadOnlyCollection<PromoVideo> PromoVideos => _promoVideos.AsReadOnly();
}
