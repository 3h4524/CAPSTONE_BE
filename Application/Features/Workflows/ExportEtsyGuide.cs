using System.Text;
using APCS.Application.Features.Shops.Common;

namespace APCS.Application.Features.Workflows;

/// <summary>A photo of the export package, by its file name in the ZIP.</summary>
public sealed record ExportGuidePhoto(string File, bool AiMade);

/// <summary>
/// The guide an export package carries for a seller who lists the product on Etsy by hand: the files in the order to
/// upload them and the same Etsy rules APCS applies when it publishes (the AI disclosure, a photo not made with AI for a
/// physical item, the production partner). Without it a package listed by hand skips what keeps the shop safe.
/// </summary>
public static class ExportEtsyGuide
{
    public const string FileName = "ETSY-LISTING-GUIDE.txt";

    public static string Build(string productName, string? productType, IReadOnlyList<ExportGuidePhoto> photos, IReadOnlyList<string> designFiles,
        string videoFile, bool hasListing, EtsyPublishConfig? etsy, bool viaPrintify)
    {
        var text = new StringBuilder();
        void Line(string value = "") => text.Append(value).Append('\n');

        Line($"How to list \"{productName}\" on Etsy yourself");
        Line(new string('=', 60));
        Line();
        if (viaPrintify)
        {
            Line("THIS WORKFLOW PUSHES TO PRINTIFY. A listing you create by hand on Etsy is not linked to Printify: its orders");
            Line("reach no one, and nothing is printed or shipped. Use Push to Printify to sell it; use this package for your own");
            Line("records, or to list it where you make the item yourself.");
            Line();
        }

        Line("1. Photos (upload in this order)");
        if (photos.Count == 0) Line("   No approved mock-up is in this package.");
        foreach (var photo in photos)
            Line($"   - {photo.File}{(photo.AiMade ? "  [made with AI]" : string.Empty)}");
        if (photos.Any(photo => photo.AiMade))
            Line("   A physical listing needs at least one photo that is not made with AI. The first one here is such a photo when the package has one: keep it first.");
        Line();

        Line("2. Video");
        Line($"   - {videoFile} (5-15 seconds, as Etsy requires).");
        Line();

        Line("3. Title, description and tags");
        if (hasListing)
        {
            Line("   - listing/title.txt: already within Etsy's title rules (140 characters, allowed characters only).");
            Line("   - listing/description.txt: keep its last paragraph. Etsy requires a listing to say when AI made the design:");
            Line($"     \"{EtsyListingText.AiDisclosure}\"");
            Line($"   - listing/tags.txt: one tag per line, at most {EtsyListingText.MaxTags} tags of {EtsyListingText.MaxTagLength} characters.");
        }
        else
        {
            Line("   No listing content was generated. Write your own, and end the description with:");
            Line($"     \"{EtsyListingText.AiDisclosure}\"");
        }
        Line();

        var digital = etsy?.Type == "download";
        Line("4. Listing type");
        if (etsy is null && !viaPrintify)
            Line("   Choose Digital download (buyers get the design files) or Physical item.");
        else if (digital)
            Line("   Digital download.");
        else
            Line($"   Physical item{(string.IsNullOrWhiteSpace(productType) ? string.Empty : $" ({productType})")}.");
        if (etsy?.Price is { } price) Line($"   Price set in the workflow: {price:0.00}.");
        if (!string.IsNullOrWhiteSpace(etsy?.TaxonomyPath)) Line($"   Etsy category: {etsy.TaxonomyPath}.");
        Line();

        Line("5. Files for buyers (digital download only)");
        if (designFiles.Count == 0) Line("   No approved design is in this package.");
        else
        {
            foreach (var file in designFiles.Take(5)) Line($"   - {file}");
            Line("   Etsy allows 5 files of up to 20 MB each on a digital listing.");
        }
        Line();

        Line("6. Before you publish");
        Line("   [ ] The description ends with the AI sentence above.");
        Line("   [ ] Who made it: \"I did\" (you designed it), with When made: made to order.");
        Line("   [ ] Physical item made by a print shop: add it under Shop Manager > Settings > Production partners, and choose it on the listing.");
        Line("   [ ] A physical item has a shipping profile and at least one photo not made with AI, shown first.");
        Line("   [ ] No brand, character, team or celebrity name in the title, tags or design that you do not have the rights to.");
        Line("   [ ] A new shop adds a few listings a day, not dozens at once.");
        return text.ToString();
    }
}
