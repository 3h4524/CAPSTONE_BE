using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a product mockup rendered from a design image.
/// </summary>
public sealed class MockupImage : CreationTrackedEntity, ISoftDeletable
{
    private MockupImage()
    {
    }

    /// <summary>
    /// Gets the source design image identifier.
    /// </summary>
    public int DesignImageId { get; private set; }

    /// <summary>
    /// Gets the target product identifier.
    /// </summary>
    public int ProductId { get; private set; }

    /// <summary>
    /// Gets the mockup template type.
    /// </summary>
    public string MockupTemplateType { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the mockup image URL.
    /// </summary>
    public string MockupImageUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the optional local mockup path.
    /// </summary>
    public string? MockupLocalPath { get; private set; }

    /// <summary>
    /// Gets the mockup width in pixels.
    /// </summary>
    public int MockupWidthPx { get; private set; }

    /// <summary>
    /// Gets the mockup height in pixels.
    /// </summary>
    public int MockupHeightPx { get; private set; }

    /// <summary>
    /// Gets the generation duration in seconds.
    /// </summary>
    public decimal? GenerationTimeSeconds { get; private set; }

    /// <summary>
    /// Gets the generation API cost in USD.
    /// </summary>
    public decimal? ApiCostUsd { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this is the final mockup.
    /// </summary>
    public bool IsFinal { get; private set; } = true;

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the source design image.
    /// </summary>
    public DesignImage DesignImage { get; private set; } = null!;

    /// <summary>
    /// Gets the target product.
    /// </summary>
    public Product Product { get; private set; } = null!;
}
