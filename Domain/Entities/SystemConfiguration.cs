using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents a runtime system configuration entry.
/// </summary>
public sealed class SystemConfiguration : AuditableEntity
{
    private SystemConfiguration()
    {
    }

    /// <summary>
    /// Gets the unique configuration key.
    /// </summary>
    public string ConfigKey { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the configuration value.
    /// </summary>
    public string ConfigValue { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the optional configuration description.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the configuration is active.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets the identifier of the administrator that last modified the entry.
    /// </summary>
    public int? LastModifiedByAdminId { get; private set; }

    /// <summary>
    /// Gets the administrator that last modified the entry.
    /// </summary>
    public Admin? LastModifiedByAdmin { get; private set; }
}
