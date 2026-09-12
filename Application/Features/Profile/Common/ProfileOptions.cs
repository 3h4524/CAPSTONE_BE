namespace APCS.Application.Features.Profile.Common;

/// <summary>
/// Allowlisted profile preference values shared by the validator and the service.
/// </summary>
/// <remarks>
/// Mirrors <c>constants/profile.ts</c> on the frontend; change both together.
/// </remarks>
public static class ProfileOptions
{
    public static readonly string DefaultTimezone = "Asia/Ho_Chi_Minh";

    public static readonly string DefaultLanguage = "vi";

    public static readonly string DefaultThemePreference = "system";

    public static readonly string[] Timezones =
    [
        "Asia/Ho_Chi_Minh",
        "Asia/Bangkok",
        "Asia/Singapore",
        "Asia/Tokyo",
        "UTC",
    ];

    public static readonly string[] Languages = ["en", "vi"];

    public static readonly string[] ThemePreferences = ["light", "dark", "system"];
}
