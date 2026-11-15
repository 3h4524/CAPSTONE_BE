using APCS.Application.Features.BatchMockups.Common;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Shops.Common;

/// <summary>
/// Etsy's listing image rules: a physical item needs a photo that shows it as it is, which for print-on-demand may be a
/// stock mock-up of the garment it is printed on. A photo AI made or edited may show the item too, but only besides one.
/// </summary>
public static class ListingPhotos
{
    /// <summary>The role of a photo of people wearing the product.</summary>
    private const string LifestyleRole = "Lifestyle";

    public const string NoRealPhoto =
        "Etsy requires a physical listing to have a photo that shows the item as it is, and every photo chosen was made with AI. Add a mock-up made on a garment template.";

    public const string AiPhotoFirst =
        "The listing opens on a photo made with AI. Etsy allows it beside a mock-up of the real garment, but buyers trust a listing more that opens on one.";

    /// <summary>
    /// Made or edited by AI: AI Studio frames, people photos an image model dressed in the product, and mock-ups on a
    /// template whose photo was itself generated.
    /// </summary>
    public static bool IsAiMade(MockupImage mockup) =>
        mockup.SourceType == MockupSources.AiGenerated
        || (mockup.SourceType == MockupSources.Uploaded && mockup.Role == LifestyleRole)
        || mockup.MockupTemplate?.AiGenerated == true;
}
