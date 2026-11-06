using APCS.Application.Abstractions.Commerce;

namespace APCS.Application.Features.Commerce;

/// <summary>
/// Builds the stored key of a market search query, so a query observed under different spacing
/// or casing maps to one row in <c>market_query_observations</c>.
/// </summary>
internal static class MarketQueryKey
{
    public const int MaximumLength = 300;

    public static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToLowerInvariant();

    public static string Platform(CommercePlatform platform) =>
        platform.ToString().ToLowerInvariant();
}
