using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a downloadable package exported from a batch job.
/// </summary>
public sealed class ExportPackage : CreationTrackedEntity
{
    private ExportPackage()
    {
    }

    /// <summary>
    /// Gets the source batch job identifier.
    /// </summary>
    public int BatchJobId { get; private set; }

    /// <summary>
    /// Gets the identifier of the seller that owns the export.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the identifiers of products included in the package.
    /// </summary>
    public int[] ProductIds { get; private set; } = [];

    /// <summary>
    /// Gets the export package type.
    /// </summary>
    public string Type { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the export package name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the optional export package description.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets package content metadata as JSON.
    /// </summary>
    public string PackageContent { get; private set; } = "{}";

    /// <summary>
    /// Gets the local package file path.
    /// </summary>
    public string? FilePath { get; private set; }

    /// <summary>
    /// Gets the package download URL.
    /// </summary>
    public string? DownloadUrl { get; private set; }

    /// <summary>
    /// Gets the package file size in megabytes.
    /// </summary>
    public decimal? FileSizeMb { get; private set; }

    /// <summary>
    /// Gets the package creation duration in seconds.
    /// </summary>
    public decimal CreationTimeSeconds { get; private set; }

    /// <summary>
    /// Gets the export preparation status.
    /// </summary>
    public string Status { get; private set; } = "preparing";

    /// <summary>
    /// Gets the UTC download timestamp.
    /// </summary>
    public DateTimeOffset? DownloadedAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC package expiration timestamp.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>
    /// Gets the source batch job.
    /// </summary>
    public BatchJob BatchJob { get; private set; } = null!;
}
