using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents an encrypted external-service API key owned by a user.
/// </summary>
public sealed class ApiKey : CreationTrackedSoftDeletableEntity
{
    private ApiKey()
    {
    }

    /// <summary>
    /// Gets the owning user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the external service provider name.
    /// </summary>
    public string ServiceProvider { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the non-secret key identifier.
    /// </summary>
    public string KeyIdentifier { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the encrypted API key value.
    /// </summary>
    public string KeyValueEncrypted { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the final four characters of the key, shown for recognition.
    /// </summary>
    public string? KeyLast4 { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the API key is active.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets the number of times the API key has been used.
    /// </summary>
    public int UsageCount { get; private set; }

    /// <summary>
    /// Gets the optional API key usage limit.
    /// </summary>
    public int? UsageLimit { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the API key was last used.
    /// </summary>
    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    /// <summary>
    /// Gets the UTC expiration timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    /// <summary>
    /// Gets the IP address from which the API key was created.
    /// </summary>
    public string? CreatedByIp { get; private set; }
}
