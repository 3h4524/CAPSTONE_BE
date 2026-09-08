namespace APCS.Common.Constants;

/// <summary>
/// Provides regex patterns shared by validation and helpers.
/// </summary>
public static class RegexPatterns
{
    /// <summary>
    /// A pragmatic email pattern for basic input screening.
    /// </summary>
    public const string Email = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    /// <summary>
    /// Characters that should be removed when creating slugs.
    /// </summary>
    public const string SlugUnsafeCharacters = @"[^a-z0-9\s-]";
}
