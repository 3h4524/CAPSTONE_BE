using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a connected Etsy shop.
/// </summary>
public sealed class EtsyIntegration : SoftDeletableEntity
{
    private readonly List<EtsyUploadLog> _uploadLogs = [];

    private EtsyIntegration()
    {
    }

    /// <summary>
    /// Gets the owning user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the external Etsy shop identifier.
    /// </summary>
    public string EtsyShopId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the encrypted Etsy OAuth access token.
    /// </summary>
    public string EtsyOAuthTokenEncrypted { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the encrypted Etsy OAuth refresh token.
    /// </summary>
    public string? EtsyRefreshTokenEncrypted { get; private set; }

    /// <summary>
    /// Gets the shop name.
    /// </summary>
    public string ShopName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this is the default Etsy shop.
    /// </summary>
    public bool IsDefault { get; private set; }

    /// <summary>
    /// Gets the number of listings created in this shop.
    /// </summary>
    public int TotalListingsCreated { get; private set; }

    /// <summary>
    /// Gets the number of listings updated in this shop.
    /// </summary>
    public int TotalListingsUpdated { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp of the last successful sync.
    /// </summary>
    public DateTimeOffset? LastSyncAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which the OAuth token expires.
    /// </summary>
    public DateTimeOffset? OAuthExpiresAtUtc { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the integration is active.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets the upload attempts made through this integration.
    /// </summary>
    public IReadOnlyCollection<EtsyUploadLog> UploadLogs => _uploadLogs.AsReadOnly();
}
