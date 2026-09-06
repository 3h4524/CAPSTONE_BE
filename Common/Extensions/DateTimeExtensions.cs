namespace APCS.Common.Extensions;

/// <summary>
/// Provides date and time helpers for stable cross-layer calculations.
/// </summary>
public static class DateTimeExtensions
{
    /// <summary>
    /// Converts a UTC date-time value to a Unix timestamp in seconds.
    /// </summary>
    /// <param name="value">The date-time value to convert.</param>
    /// <returns>The Unix timestamp in seconds.</returns>
    public static long ToUnixTimestamp(this DateTimeOffset value) => value.ToUnixTimeSeconds();

    /// <summary>
    /// Returns true when the value is less than or equal to the supplied clock value.
    /// </summary>
    /// <param name="value">The expiration value.</param>
    /// <param name="utcNow">The current UTC clock value.</param>
    /// <returns>True when expired.</returns>
    public static bool IsExpired(this DateTimeOffset value, DateTimeOffset utcNow) => value <= utcNow;
}
