using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents an AI-generated design image.
/// </summary>
public sealed class DesignImage : CreationTrackedEntity, ISoftDeletable
{
    private readonly List<MockupImage> _mockupImages = [];

    private DesignImage()
    {
    }

    /// <summary>
    /// Gets the target product identifier.
    /// </summary>
    public int ProductId { get; private set; }

    /// <summary>
    /// Gets the source AI prompt identifier.
    /// </summary>
    public int AiPromptId { get; private set; }

    /// <summary>
    /// Gets the source batch job identifier, when applicable.
    /// </summary>
    public int? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the image-generation model name.
    /// </summary>
    public string ImageGeneratorModel { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the external API response identifier.
    /// </summary>
    public string? ApiResponseId { get; private set; }

    /// <summary>
    /// Gets the generated image URL.
    /// </summary>
    public string ImageUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the optional local image path.
    /// </summary>
    public string? ImageLocalPath { get; private set; }

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
    /// Gets the computed image quality score.
    /// </summary>
    public decimal? QualityScore { get; private set; }

    /// <summary>
    /// Gets the optional seller rating.
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
    /// Gets the generation API cost in USD.
    /// </summary>
    public decimal ApiCostUsd { get; private set; }

    /// <summary>
    /// Gets generation metadata as JSON.
    /// </summary>
    public string? GenerationMetadata { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

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
    /// Gets the mockups generated from this design.
    /// </summary>
    public IReadOnlyCollection<MockupImage> MockupImages => _mockupImages.AsReadOnly();
}
