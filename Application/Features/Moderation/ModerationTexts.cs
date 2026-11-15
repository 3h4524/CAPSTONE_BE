using APCS.Application.Abstractions.Moderation;

namespace APCS.Application.Features.Moderation;

/// <summary>
/// The untrusted text fields of each guarded subject, named as the seller sees them. Only text that reaches a provider
/// prompt is listed (a product's source notes do not).
/// </summary>
public static class ModerationTexts
{
    public static IReadOnlyList<ModerationText> Product(string? name, string? niche, string? keywords, string? description,
        string? designText, string? customPrompt) =>
    [
        new("the product name", name),
        new("the niche", niche),
        new("the keywords", keywords),
        new("the product description", description),
        new("the design text", designText),
        new("the custom image prompt", customPrompt)
    ];
}
