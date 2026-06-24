using Microsoft.AspNetCore.Identity;

namespace APCS.Infrastructure.Persistence;

/// <summary>
/// Represents an ASP.NET Core Identity user for APCS.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// Gets or sets the user's full display name.
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>
    /// Gets or sets the avatar URL.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Gets or sets the user's time zone.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// Gets or sets the user's preferred language.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the account is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the last successful login timestamp.
    /// </summary>
    public DateTimeOffset? LastLoginAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
