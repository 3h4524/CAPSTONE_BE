using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a product mockup rendered from a design image.
/// </summary>
public sealed class MockupImage : CreationTrackedSoftDeletableEntity
{
    private readonly List<PromoVideoScene> _promoVideoScenes = [];

    private MockupImage()
    {
    }

    /// <summary>
    /// Gets the source design image identifier.
    /// </summary>
    public Guid DesignImageId { get; private set; }

    /// <summary>
    /// Gets the target product identifier.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Gets the mockup template identifier.
    /// </summary>
    public Guid MockupTemplateId { get; private set; }

    /// <summary>
    /// Gets the provider call that produced this mockup, when recorded.
    /// </summary>
    public Guid? ApiUsageRecordId { get; private set; }

    /// <summary>
    /// Gets the object-storage provider holding the file.
    /// </summary>
    public string StorageProvider { get; private set; } = "s3";

    /// <summary>
    /// Gets the object-storage key.
    /// </summary>
    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the mockup image URL.
    /// </summary>
    public string MockupImageUrl { get; private set; } = string.Empty;

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
    /// Gets the mockup approval status.
    /// </summary>
    public string ApprovalStatus { get; private set; } = "pending";

    /// <summary>
    /// Gets a value indicating whether this is the final mockup.
    /// </summary>
    public bool IsFinal { get; private set; } = true;

    /// <summary>
    /// Gets the source design image.
    /// </summary>
    public DesignImage DesignImage { get; private set; } = null!;

    /// <summary>
    /// Gets the target product.
    /// </summary>
    public Product Product { get; private set; } = null!;

    /// <summary>
    /// Gets the mockup template used for the render.
    /// </summary>
    public MockupTemplate MockupTemplate { get; private set; } = null!;

    /// <summary>
    /// Gets the provider call that produced this mockup, when recorded.
    /// </summary>
    public ApiUsageRecord? ApiUsageRecord { get; private set; }

    /// <summary>
    /// Gets the promotional video scenes using this mockup.
    /// </summary>
    public IReadOnlyCollection<PromoVideoScene> PromoVideoScenes => _promoVideoScenes.AsReadOnly();
}
