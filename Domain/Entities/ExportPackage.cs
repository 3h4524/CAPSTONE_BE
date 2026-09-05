using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a downloadable bundle of generated assets.
/// </summary>
public sealed class ExportPackage : CreationTrackedEntity
{
    private readonly List<ExportPackageItem> _items = [];

    private ExportPackage()
    {
    }

    /// <summary>
    /// Gets the source batch job identifier, when applicable.
    /// </summary>
    public Guid? BatchJobId { get; private set; }

    /// <summary>
    /// Gets the owning user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the package type.
    /// </summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the package name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the package description.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets the object-storage provider holding the file.
    /// </summary>
    public string? StorageProvider { get; private set; } = "s3";

    /// <summary>
    /// Gets the object-storage key.
    /// </summary>
    public string? StorageKey { get; private set; }

    /// <summary>
    /// Gets the download URL.
    /// </summary>
    public string? DownloadUrl { get; private set; }

    /// <summary>
    /// Gets the package size in megabytes.
    /// </summary>
    public decimal? FileSizeMb { get; private set; }

    /// <summary>
    /// Gets the packaging duration in seconds.
    /// </summary>
    public decimal? CreationTimeSeconds { get; private set; }

    /// <summary>
    /// Gets the package status.
    /// </summary>
    public string Status { get; private set; } = "preparing";

    /// <summary>
    /// Gets the UTC timestamp when the package was downloaded.
    /// </summary>
    public DateTimeOffset? DownloadedAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp after which the download link stops working.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>
    /// Gets the source batch job, when applicable.
    /// </summary>
    public BatchJob? BatchJob { get; private set; }

    /// <summary>
    /// Gets the products included in the package.
    /// </summary>
    public IReadOnlyCollection<ExportPackageItem> Items => _items.AsReadOnly();
}
