using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Stores the shop and preference settings that belong to a user account.
/// </summary>
public sealed class UserProfile : AuditableEntity
{
    private UserProfile()
    {
    }

    /// <summary>
    /// Gets the owning user identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Gets the shop name.
    /// </summary>
    public string? ShopName { get; private set; }

    /// <summary>
    /// Gets the shop description.
    /// </summary>
    public string? ShopDescription { get; private set; }

    /// <summary>
    /// Gets the preferred time zone.
    /// </summary>
    public string TimeZone { get; private set; } = "UTC";

    /// <summary>
    /// Gets the preferred language.
    /// </summary>
    public string Language { get; private set; } = "en";

    /// <summary>
    /// Gets the preferred UI theme.
    /// </summary>
    public string ThemePreference { get; private set; } = "light";

    /// <summary>
    /// Gets a value indicating whether email notifications are enabled.
    /// </summary>
    public bool NotificationEmailEnabled { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the user subscribed to the newsletter.
    /// </summary>
    public bool NewsletterSubscribed { get; private set; }

    /// <summary>
    /// Gets the profile completion percentage.
    /// </summary>
    public decimal ProfileCompletionPercentage { get; private set; }
}
