namespace APCS.Application.Features.UsageStatistics;

/// <summary>
/// The <c>api_usage_records.feature</c> values the application writes.
/// </summary>
/// <remarks>
/// The database's <c>chk_api_usage_feature</c> constraint allows only image_generation, mockup,
/// video, listing, seo and integration, so a new AI feature reuses the closest of those rather
/// than adding its own value.
/// </remarks>
public static class UsageFeatures
{
    public const string ImageGeneration = "image_generation";
    public const string Listing = "listing";

    /// <summary>Lifestyle mockups, the one AI feature that makes a mockup.</summary>
    public const string Mockup = "mockup";
}
