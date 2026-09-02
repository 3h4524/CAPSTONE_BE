using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a seller's connection to a Printify store.
/// </summary>
public sealed class PrintifyIntegration : SoftDeletableEntity
{
    private readonly List<PrintifyUploadLog> _uploadLogs = [];

    private PrintifyIntegration()
    {
    }

    /// <summary>
    /// Gets the owning seller identifier.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the Printify store identifier.
    /// </summary>
    public string PrintifyStoreId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the encrypted Printify API token.
    /// </summary>
    public string PrintifyApiTokenEncrypted { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the Printify shop name.
    /// </summary>
    public string ShopName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the optional Printify shop title.
    /// </summary>
    public string? ShopTitle { get; private set; }

    /// <summary>
    /// Gets the total number of uploaded products.
    /// </summary>
    public int TotalProductsUploaded { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp of the last synchronization.
    /// </summary>
    public DateTimeOffset? LastSyncAtUtc { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the integration is active.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets upload attempts made through this integration.
    /// </summary>
    public IReadOnlyCollection<PrintifyUploadLog> UploadLogs => _uploadLogs.AsReadOnly();
}
