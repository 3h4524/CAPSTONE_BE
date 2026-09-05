using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents an AI-generated design image.
/// </summary>
public sealed class DesignImage : CreationTrackedSoftDeletableEntity
{
    private readonly List<MockupImage> _mockupImages = [];
    private readonly List<PromoVideoScene> _promoVideoScenes = [];

    private DesignImage()
    {
    }

    /// <summary>
    /// Gets the target product identifier.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Gets the source AI prompt identifier.
    /// </summary>
    public Guid AiPromptId { get; private set; }

    /// <summary>
    /// Gets the source batch job identifier, when applicable.
    /// </summary>
    public Guid? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the provider call that produced this image, when recorded.
    /// </summary>
    public Guid? ApiUsageRecordId { get; private set; }

    /// <summary>
    /// Gets the image-generation model name.
    /// </summary>
    public string ImageGeneratorModel { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the object-storage provider holding the file.
    /// </summary>
    public string StorageProvider { get; private set; } = "s3";

    /// <summary>
    /// Gets the object-storage key.
    /// </summary>
    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the generated image URL.
    /// </summary>
    public string ImageUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the image width in pixels.
    /// </summary>
    public int ImageWidthPx { get; private set; }

    /// <summary>
    /// Gets the image height in pixels.
    /// </summary>
    public int ImageHeightPx { get; private set; }

    /// <summary>
    /// Gets the image file format.
    /// </summary>
    public string FileFormat { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the image file size in megabytes.
    /// </summary>
    public decimal FileSizeMb { get; private set; }

    /// <summary>
    /// Gets the computed image quality score, on a zero-to-one scale.
    /// </summary>
    public decimal? QualityScore { get; private set; }

    /// <summary>
    /// Gets the optional seller rating, from one to five.
    /// </summary>
    public int? UserRating { get; private set; }

    /// <summary>
    /// Gets the image approval status.
    /// </summary>
    public string ApprovalStatus { get; private set; } = "pending";

    /// <summary>
    /// Gets a value indicating whether this is the final image.
    /// </summary>
    public bool IsFinal { get; private set; }

    /// <summary>
    /// Gets the variation index.
    /// </summary>
    public int VariationIndex { get; private set; } = 1;

    /// <summary>
    /// Gets the generation duration in seconds.
    /// </summary>
    public decimal GenerationTimeSeconds { get; private set; }

    /// <summary>
    /// Gets generation metadata as JSON.
    /// </summary>
    public string? GenerationMetadata { get; private set; }

    /// <summary>
    /// Gets the target product.
    /// </summary>
    public Product Product { get; private set; } = null!;

    /// <summary>
    /// Gets the source AI prompt.
    /// </summary>
    public AiPrompt AiPrompt { get; private set; } = null!;

    /// <summary>
    /// Gets the source batch job, when applicable.
    /// </summary>
    public BatchJob? BatchJob { get; private set; }

    /// <summary>
    /// Gets the provider call that produced this image, when recorded.
    /// </summary>
    public ApiUsageRecord? ApiUsageRecord { get; private set; }

    /// <summary>
    /// Gets the mockups generated from this design.
    /// </summary>
    public IReadOnlyCollection<MockupImage> MockupImages => _mockupImages.AsReadOnly();

    /// <summary>
    /// Gets the promotional video scenes using this design.
    /// </summary>
    public IReadOnlyCollection<PromoVideoScene> PromoVideoScenes => _promoVideoScenes.AsReadOnly();
}
