using APCS.Domain.Common;

namespace APCS.Domain.Entities;

/// <summary>
/// Stores seller-specific shop and preference settings.
/// </summary>
public sealed class SellerProfile : AuditableEntity
{
    private SellerProfile()
    {
    }

    /// <summary>
    /// Gets the owning seller identifier.
    /// </summary>
    public int SellerId { get; private set; }

    /// <summary>
    /// Gets the seller's shop name.
    /// </summary>
    public string? ShopName { get; private set; }

    /// <summary>
    /// Gets the seller's shop description.
    /// </summary>
    public string? ShopDescription { get; private set; }

    /// <summary>
    /// Gets a value indicating whether email notifications are enabled.
    /// </summary>
    public bool NotificationEmailEnabled { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the seller subscribed to newsletters.
    /// </summary>
    public bool NewsletterSubscribed { get; private set; }

    /// <summary>
    /// Gets a value indicating whether two-factor authentication is enabled.
    /// </summary>
    public bool TwoFactorEnabled { get; private set; }

    /// <summary>
    /// Gets the default time zone.
    /// </summary>
    public string DefaultTimeZone { get; private set; } = "UTC";

    /// <summary>
    /// Gets the default language.
    /// </summary>
    public string DefaultLanguage { get; private set; } = "en";

    /// <summary>
    /// Gets the preferred user-interface theme.
    /// </summary>
    public string ThemePreference { get; private set; } = "light";

    /// <summary>
    /// Gets the profile completion percentage.
    /// </summary>
    public decimal ProfileCompletionPercentage { get; private set; }
}
