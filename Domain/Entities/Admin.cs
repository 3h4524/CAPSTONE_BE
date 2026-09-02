using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Represents an administrative APCS account.
/// </summary>
public sealed class Admin : AuditableEntity
{
    private readonly List<SystemConfiguration> _modifiedConfigurations = [];
    private readonly List<AuditLog> _auditLogs = [];
    private readonly List<SupportTicket> _assignedTickets = [];

    private Admin()
    {
    }

    /// <summary>
    /// Gets the administrator email address.
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the administrator password hash.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the administrator full name.
    /// </summary>
    public string FullName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the administrator role.
    /// </summary>
    public string Role { get; private set; } = string.Empty;

    /// <summary>
    /// Gets administrator permissions as JSON.
    /// </summary>
    public string Permissions { get; private set; } = "{}";

    /// <summary>
    /// Gets a value indicating whether the administrator is active.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets the UTC timestamp of the last successful login.
    /// </summary>
    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    /// <summary>
    /// Gets configurations last modified by this administrator.
    /// </summary>
    public IReadOnlyCollection<SystemConfiguration> ModifiedConfigurations => _modifiedConfigurations.AsReadOnly();

    /// <summary>
    /// Gets audit entries associated with this administrator.
    /// </summary>
    public IReadOnlyCollection<AuditLog> AuditLogs => _auditLogs.AsReadOnly();

    /// <summary>
    /// Gets support tickets assigned to this administrator.
    /// </summary>
    public IReadOnlyCollection<SupportTicket> AssignedTickets => _assignedTickets.AsReadOnly();
}
