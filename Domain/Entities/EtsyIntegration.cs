using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a seller's connection to an Etsy shop.
/// </summary>
public sealed class EtsyIntegration : SoftDeletableEntity
{
    private readonly List<EtsyUploadLog> _uploadLogs = [];

    private EtsyIntegration()
    {
    }

    /// <summary>
    /// Gets the owning seller identifier.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the external Etsy shop identifier.
    /// </summary>
    public string EtsyShopId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the encrypted Etsy OAuth token.
    /// </summary>
    public string EtsyOAuthTokenEncrypted { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the Etsy shop name.
    /// </summary>
    public string ShopName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the total number of created listings.
    /// </summary>
    public int TotalListingsCreated { get; private set; }

    /// <summary>
    /// Gets the total number of updated listings.
    /// </summary>
    public int TotalListingsUpdated { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp of the last synchronization.
    /// </summary>
    public DateTimeOffset? LastSyncAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC OAuth expiration timestamp.
    /// </summary>
    public DateTimeOffset? OAuthExpiresAtUtc { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the integration is active.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets upload attempts made through this integration.
    /// </summary>
    public IReadOnlyCollection<EtsyUploadLog> UploadLogs => _uploadLogs.AsReadOnly();
}
