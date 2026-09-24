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
            product?.Name?.Trim() ?? string.Empty,
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
            style?.StyleModifiers ?? string.Empty);
    }
}
