namespace APCS.Application.Features.Profile.Dtos.Response;

/// <summary>
/// Represents the authenticated user's profile.
/// </summary>
/// <param name="FullName">The account holder's full name.</param>
/// <param name="Email">The sign-in email address.</param>
/// <param name="AvatarUrl">The read-only avatar URL.</param>
/// <param name="ShopName">The shop display name.</param>
/// <param name="ShopDescription">A short shop description.</param>
/// <param name="Timezone">An IANA timezone from the allowlist.</param>
/// <param name="Language">An interface language code from the allowlist.</param>
/// <param name="ThemePreference">A theme preference from the allowlist.</param>
/// <param name="NotificationEmailEnabled">Whether account emails are sent.</param>
/// <param name="NewsletterSubscribed">Whether the newsletter is sent.</param>
/// <param name="TwoFactorEnabled">Whether two-factor authentication is on.</param>
public sealed record ProfileResponseDto(
    string FullName,
    string Email,
    string? AvatarUrl,
    string? ShopName,
    string? ShopDescription,
    string Timezone,
    string Language,
    string? ThemePreference,
    bool NotificationEmailEnabled,
    bool NewsletterSubscribed,
    bool TwoFactorEnabled);
