using System.Text.Json;

namespace APCS.Application.Features.Commerce;

/// <summary>
/// Etsy search returns digital downloads (sewing patterns, mockups, SVG files) for product keywords
/// such as "oversized sweatshirt". They are not the physical product a seller would make, and their
/// low prices and different buyers would distort the niche signals, so they are left out of them.
/// </summary>
public static class MarketListingKind
{
    public const string DigitalListingType = "download";

    /// <summary>The jsonb fragment that marks a digital listing, for database containment filters.</summary>
    public const string DigitalListingJson = "{\"listing_type\":\"download\"}";

    public static bool IsDigital(string? rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
            return false;

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("listing_type", out var listingType)
                && listingType.ValueKind == JsonValueKind.String
                && string.Equals(listingType.GetString(), DigitalListingType, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
