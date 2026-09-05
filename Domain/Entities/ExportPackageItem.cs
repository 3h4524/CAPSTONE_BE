using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents one product inside an export package, and which assets to include for it.
/// </summary>
public sealed class ExportPackageItem : CreationTrackedEntity
{
    private ExportPackageItem()
    {
    }

    /// <summary>
    /// Gets the owning export package identifier.
    /// </summary>
    public Guid ExportPackageId { get; private set; }

    /// <summary>
    /// Gets the included product identifier.
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// Gets a value indicating whether design images are included.
    /// </summary>
    public bool IncludeDesignImages { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether mockup images are included.
    /// </summary>
    public bool IncludeMockupImages { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the promotional video is included.
    /// </summary>
    public bool IncludePromoVideo { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the listing content is included.
    /// </summary>
    public bool IncludeListingContent { get; private set; } = true;

    /// <summary>
    /// Gets the folder path this product occupies inside the archive.
    /// </summary>
    public string? FolderPathInZip { get; private set; }

    /// <summary>
    /// Gets the packaging status for this product.
    /// </summary>
    public string ItemStatus { get; private set; } = "pending";

    /// <summary>
    /// Gets the packaging error message, when packaging failed.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Gets the owning export package.
    /// </summary>
    public ExportPackage ExportPackage { get; private set; } = null!;

    /// <summary>
    /// Gets the included product.
    /// </summary>
    public Product Product { get; private set; } = null!;
}
