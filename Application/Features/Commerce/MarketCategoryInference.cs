using System.Text.RegularExpressions;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Commerce;

/// <summary>
/// Etsy's listings/active response carries only a numeric taxonomy_id, so collected opportunities
/// have no category name. The category is read from the niche instead: every collected niche names
/// the product type ("custom hoodie", "embroidered tote bag"). Demo rows keep their own category.
/// </summary>
public static class MarketCategoryInference
{
    public const string Apparel = "apparel";
    public const string Accessories = "accessories";

    // Accessories first: "denim jacket patch" is a patch, not a jacket.
    private static readonly (string Category, HashSet<string> Words)[] Rules =
    [
        (Accessories, new(StringComparer.Ordinal) { "hat", "hats", "cap", "caps", "beanie", "beanies", "tote", "bag", "bags", "apron", "aprons", "patch", "patches" }),
        (Apparel, new(StringComparer.Ordinal)
        {
            "shirt", "shirts", "tee", "tees", "sweatshirt", "sweatshirts", "hoodie", "hoodies", "sweater", "sweaters",
            "cardigan", "jacket", "jackets", "jersey", "bodysuit", "onesie", "top", "tops", "dress", "dresses"
        })
    ];

    public static string? Resolve(MarketOpportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        return string.IsNullOrWhiteSpace(opportunity.Category)
            ? FromText(opportunity.Niche) ?? FromText(opportunity.Title)
            : opportunity.Category;
    }

    public static string? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var words = Regex.Split(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+");
        foreach (var (category, categoryWords) in Rules)
        {
            if (words.Any(categoryWords.Contains))
                return category;
        }

        return null;
    }
}
