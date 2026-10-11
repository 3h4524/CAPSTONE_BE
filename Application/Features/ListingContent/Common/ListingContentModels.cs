namespace APCS.Application.Features.ListingContent.Common;

public sealed record ListingContentInput(
    Guid ProductId,
    string Name,
    string ProductType,
    string? Niche,
    string? TargetAudience,
    string Description,
    IReadOnlyList<string> Keywords,
    string? Notes);

public sealed record GeneratedListingContent(
    string Title,
    string Description,
    IReadOnlyList<GeneratedListingTag> Tags);

public static class ListingContentPolicyDefaults
{
    public const string Title = "Default marketplace listing standards";

    public const string Content = """
        Listing content standards:
        - Use accurate, buyer-focused language based only on the supplied product data.
        - Write a clear, concise, keyword-relevant title without keyword stuffing.
        - Generate exactly 13 unique tags when the marketplace supports 13 tags; keep each tag concise (20 characters or fewer for Etsy).
        - Use diverse tags that describe the product, audience, style, use case, occasion, and gift intent where relevant.
        - Do not repeat attributes already covered by marketplace fields unless the phrase adds useful search meaning.
        - Keep the description readable and helpful: explain what the product is, who it is for, and the relevant use or occasion.
        - Never invent materials, dimensions, colors, features, guarantees, certifications, or other claims not present in the product data.
        - Do not use prohibited, misleading, discriminatory, or spam-like terms.
        - Return content in the requested language and make the result sound natural to a human buyer.
        """;
}

public sealed record GeneratedListingTag(string Value, string? Type = null);
