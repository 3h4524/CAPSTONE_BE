using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.DesignTemplates.Common;
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
        if (product?.CustomPrompt?.Trim() is { Length: > 0 } customPrompt)
            return CustomPromptComponents(product, customPrompt);

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

        var keywords = ArtworkPromptRules.SplitKeywords(product?.MainKeywords);
        return new PromptComponents(
            Subject(product, template),
            // Without a style preset the template's own art style fills {style}, rather than leaving a gap in the sentence.
            styleName.Length > 0 ? styleName : TemplateStyle(template),
            // Keywords about the feel ("funny", "vintage") become the default mood, which the Seller sees and can change.
            keywords.Mood,
            string.Empty,
            string.Empty,
            template?.BasePrompt ?? string.Empty,
            product?.NicheCategory?.Trim() is { Length: > 0 } niche
                ? niche
                : keywords.Motifs.Length > 0
                    ? keywords.Motifs
                    : PromptRules.FallbackNiche,
            style?.StyleModifiers ?? string.Empty,
            keywords.Motifs,
            UserDescription(product));
    }

    /// <summary>The sentence that makes a custom prompt print the design text, as a lettering template's quoted subject does.</summary>
    public const string CustomPhraseSentence = "The design prints the exact phrase \"{subject}\".";

    // The Seller's own prompt replaces the template, and with it the keywords and description it would have drawn from.
    // It sets the look too, so no style preset is added: one a job applies to every product ("Minimalist: soft neutral
    // tones") would wash out a prompt that asks for a stormy sky.
    // The design text is the only lettering: the prompt gets the quoted phrase a lettering template carries, so every
    // lettering rule applies to it; without design text the design prints no words, not the product name.
    private static PromptComponents CustomPromptComponents(Product product, string customPrompt)
    {
        var designText = product.DesiredDesignText?.Trim() ?? string.Empty;
        // Words the Seller quoted in the prompt are printed only when they are the design text.
        customPrompt = ArtworkPromptRules.WithoutOtherQuotedText(customPrompt, designText);
        var basePrompt = designText.Length > 0 ? $"{customPrompt.TrimEnd()} {CustomPhraseSentence}" : customPrompt;
        return new PromptComponents(
            designText,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            basePrompt,
            product.NicheCategory?.Trim() ?? string.Empty,
            OwnPrompt: true);
    }

    // A template that prints the subject as an exact phrase prints the Seller's design text when there is one:
    // the product name is a listing title ("Minimalist Mountain Sunrise"), not necessarily the words to print.
    private static string Subject(Product? product, DesignTemplate? template) =>
        ArtworkPromptRules.PrintsExactPhrase(template?.BasePrompt) && product?.DesiredDesignText?.Trim() is { Length: > 0 } designText
            ? designText
            : CleanSubject(product);

    private static string TemplateStyle(DesignTemplate? template) =>
        template?.ArtStyle is { } artStyle && DesignTemplateArtStyles.Labels.TryGetValue(artStyle, out var label)
            ? label.ToLowerInvariant()
            : string.Empty;

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
