using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a connected Printify shop.
/// </summary>
public sealed class PrintifyIntegration : SoftDeletableEntity
{
    private readonly List<PrintifyUploadLog> _uploadLogs = [];

    private PrintifyIntegration()
    {
    }

    /// <summary>
    /// Gets the owning user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the external Printify store identifier.
    /// </summary>
    public string PrintifyStoreId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the encrypted Printify API token.
    /// </summary>
    public string PrintifyApiTokenEncrypted { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the shop name.
    /// </summary>
    public string ShopName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the shop title.
    /// </summary>
    public string? ShopTitle { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this is the default Printify shop.
    /// </summary>
    public bool IsDefault { get; private set; }

    /// <summary>
    /// Gets the number of products uploaded to this shop.
    /// </summary>
    public int TotalProductsUploaded { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp of the last successful sync.
    /// </summary>
    public DateTimeOffset? LastSyncAtUtc { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the integration is active.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets the upload attempts made through this integration.
    /// </summary>
    public IReadOnlyCollection<PrintifyUploadLog> UploadLogs => _uploadLogs.AsReadOnly();
}
