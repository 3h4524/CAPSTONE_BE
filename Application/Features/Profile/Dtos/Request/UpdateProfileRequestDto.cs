namespace APCS.Application.Features.Profile.Dtos.Request;

/// <summary>
/// Partially updates the authenticated user's profile.
/// </summary>
/// <remarks>
/// Every property is optional so PATCH sends only what changed. Avatar is read-only here and
/// therefore absent: photo upload waits for the Cloudinary flow. Server-owned fields
/// (<c>UpdatedAt</c>, <c>ProfileCompletionPercentage</c>) are never accepted from the client.
/// </remarks>
/// <param name="FullName">The account holder's full name.</param>
/// <param name="Email">The sign-in email address.</param>
/// <param name="ShopName">The shop display name.</param>
/// <param name="ShopDescription">A short shop description.</param>
/// <param name="Timezone">An IANA timezone from the allowlist.</param>
/// <param name="Language">An interface language code from the allowlist.</param>
/// <param name="ThemePreference">A theme preference from the allowlist.</param>
/// <param name="NotificationEmailEnabled">Whether account emails are sent.</param>
/// <param name="NewsletterSubscribed">Whether the newsletter is sent.</param>
/// <param name="TwoFactorEnabled">Whether two-factor authentication is on.</param>
public sealed record UpdateProfileRequestDto(
    string? FullName = null,
    string? Email = null,
    string? ShopName = null,
    string? ShopDescription = null,
    string? Timezone = null,
    string? Language = null,
    string? ThemePreference = null,
    bool? NotificationEmailEnabled = null,
    bool? NewsletterSubscribed = null,
    bool? TwoFactorEnabled = null);
