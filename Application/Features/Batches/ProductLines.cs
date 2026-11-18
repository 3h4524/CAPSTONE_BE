namespace APCS.Application.Features.Batches;

/// <summary>
/// The line of products a batch makes. Each line prints its artwork its own way (a transparent chest graphic, a mug wrap,
/// a full-bleed poster, a tall phone case), is shown with its own mock-ups and videos, and becomes its own Printify
/// product and Etsy listing, so a batch makes products of one line only. The line is the line of the batch's product
/// type; T-shirts and hoodies share one, so one design can go on both.
/// </summary>
public static class ProductLines
{
    public const string Apparel = "apparel";
    public const string Bags = "bags";
    public const string Drinkware = "drinkware";
    public const string WallArt = "wall_art";
    public const string PhoneCases = "phone_cases";

    private static readonly Dictionary<string, string> LineOfType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tshirt"] = Apparel,
        ["hoodie"] = Apparel,
        ["tote_bag"] = Bags,
        ["mug"] = Drinkware,
        ["poster"] = WallArt,
        ["phone_case"] = PhoneCases,
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        [Apparel] = "Apparel",
        [Bags] = "Tote bags",
        [Drinkware] = "Mugs",
        [WallArt] = "Wall art",
        [PhoneCases] = "Phone cases",
    };

    /// <summary>The line a product type belongs to, or null for an unknown type.</summary>
    public static string? Of(string? productType) =>
        productType is not null && LineOfType.TryGetValue(productType.Trim(), out var line) ? line : null;

    /// <summary>The product types of <paramref name="line"/>, in the order they are offered.</summary>
    public static IReadOnlyList<string> TypesOf(string? line) =>
        LineOfType.Where(pair => pair.Value == line).Select(pair => pair.Key).ToList();

    private static readonly Dictionary<string, string> TypeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tshirt"] = "T-shirt",
        ["hoodie"] = "Hoodie",
        ["tote_bag"] = "Tote bag",
        ["mug"] = "Mug",
        ["poster"] = "Poster",
        ["phone_case"] = "Phone case",
    };

    public static string Label(string? line) => line is not null && Labels.TryGetValue(line, out var label) ? label : "no line";

    /// <summary>A product type as the Seller reads it ("Tote bag"), the code itself for an unknown one.</summary>
    public static string TypeLabel(string productType) => TypeLabels.TryGetValue(productType.Trim(), out var label) ? label : productType;

    /// <summary>Why a product of <paramref name="productType"/> cannot join a batch of <paramref name="line"/>. Worded as the import dialog words it.</summary>
    public static string WrongLineMessage(string line, string productType) =>
        $"This is a {Label(line).ToLowerInvariant()} batch: use {string.Join(" or ", TypesOf(line).Select(TypeLabel))}, "
        + $"or put this {TypeLabel(productType)} in a batch of its own line.";

    /// <summary>One line for the assistant's tool descriptions: which types go together in a batch.</summary>
    public static string Summary =>
        string.Join("; ", Labels.Keys.Select(line => $"{Label(line)}: {string.Join(", ", TypesOf(line))}"));
}
