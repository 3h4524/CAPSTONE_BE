using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.BatchProductPrompts.Common;

/// <summary>
/// Resolves the default prompt components (design template base prompt + style preset modifiers)
/// for a product. Shared by <see cref="BatchProductPromptService"/> (prompt-editor preview) and
/// <see cref="APCS.Application.Features.DesignGeneration.DesignGenerationService"/> (the actual
/// prompt synthesized and sent to the AI provider), so both stay in sync.
/// </summary>
public static class PromptDefaultsResolver
{
    public static async Task<PromptComponents> ResolveAsync(
        Product? product,
        IRepository<DesignTemplate> templates,
        IRepository<StyleArtPreset> styles,
        CancellationToken cancellationToken)
    {
        var template = product?.DesignTemplateId.HasValue == true
            ? await templates.Query()
                .Where(item => item.Id == product!.DesignTemplateId!.Value)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var styleName = product?.StylePreset?.Trim() ?? string.Empty;
        var style = string.IsNullOrEmpty(styleName)
            ? null
            : await styles.Query()
                .Where(item => item.IsActive && item.Name.ToLower() == styleName.ToLowerInvariant())
                .SingleOrDefaultAsync(cancellationToken);

        return new PromptComponents(
            CleanSubject(product),
            styleName,
            string.Empty,
            string.Empty,
            string.Empty,
            template?.BasePrompt ?? string.Empty,
            product?.NicheCategory?.Trim() is { Length: > 0 } niche
                ? niche
                : product?.MainKeywords?.Trim() is { Length: > 0 } keywords
                    ? keywords
                    : PromptRules.FallbackNiche,
            style?.StyleModifiers ?? string.Empty,
            product?.MainKeywords?.Trim() ?? string.Empty,
            UserDescription(product));
    }

    private static readonly string[] ProductTypeWords =
        ["t-shirt", "t shirt", "tshirt", "hoodie", "mug", "poster", "tote bag", "tote_bag", "phone case", "phone_case"];

    // The subject is the product name, but a trailing product word ("... T-Shirt") makes the image
    // model draw the product instead of just the artwork, so it is stripped when something remains.
    private static string CleanSubject(Product? product)
    {
        var name = product?.Name?.Trim() ?? string.Empty;
        foreach (var word in ProductTypeWords)
        {
            if (name.EndsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                var stripped = name[..^word.Length].TrimEnd(' ', '-', ',');
                if (stripped.Length > 0) return stripped;
            }
        }
        return name;
    }

    // BatchService stores a generated "name; type: ...; niche: ...; keywords: ..." fallback when the
    // Seller left the description empty; that carries no extra information, so it is skipped.
    private static string UserDescription(Product? product)
    {
        var description = product?.InputDescription?.Trim() ?? string.Empty;
        return description.Length == 0 || description.StartsWith($"{product!.Name}; type:", StringComparison.Ordinal)
            ? string.Empty
            : description;
    }
}
