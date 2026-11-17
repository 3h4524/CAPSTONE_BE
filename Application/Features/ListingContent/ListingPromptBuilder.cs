using System.Text;
using APCS.Application.Features.ListingContent.Common;
using APCS.Application.Features.Rag.Dtos;
using APCS.Application.Features.Shops.Common;
using System.Text.Json.Nodes;

namespace APCS.Application.Features.ListingContent;

public static class ListingPromptBuilder
{
    /// <summary>Tags asked for: a few more than Etsy keeps, so one that is too long does not fail the listing.</summary>
    public const int TagCandidates = 16;

    /// <summary>The JSON shape Gemini must answer with.</summary>
    public static JsonNode ResponseSchema() => JsonNode.Parse($$"""
        {
          "type": "OBJECT",
          "properties": {
            "title": { "type": "STRING" },
            "description": { "type": "STRING" },
            "tags": {
              "type": "ARRAY",
              "minItems": {{EtsyListingText.MaxTags}},
              "maxItems": {{TagCandidates}},
              "items": {
                "type": "OBJECT",
                "properties": {
                  "value": { "type": "STRING" },
                  "type": { "type": "STRING" }
                },
                "required": ["value", "type"]
              }
            }
          },
          "required": ["title", "description", "tags"]
        }
        """)!;

    public static string Build(
        ListingContentInput input,
        IReadOnlyList<RagSearchResultDto> policyContext,
        IReadOnlyList<RagSearchResultDto> listingContext)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Generate marketplace listing content from the product information below.");
        builder.AppendLine("Return only valid JSON with this exact shape:");
        builder.AppendLine("{\"title\":\"string\",\"description\":\"string\",\"tags\":[{\"value\":\"string\",\"type\":\"string\"}]}");
        builder.AppendLine("Return JSON only. Do not include Markdown, code fences, commentary, or additional fields.");
        builder.AppendLine("Rules:");
        builder.AppendLine("- Use English unless the product data clearly requests another language.");
        builder.AppendLine("- Do not invent product features, materials, sizes, or claims.");
        builder.AppendLine("- Use the reference context only as guidance; do not copy it verbatim.");
        builder.AppendLine("- When the design has printed text, use those exact words in the title or the description's first sentence, and in a tag when they fit: buyers search for them.");
        builder.AppendLine("- The artwork line is the prompt the design image was made from: use it only to describe the artwork's subject and style, never as facts about the product (material, sizes, fit).");
        builder.AppendLine("Title (Etsy):");
        builder.AppendLine($"- At most {EtsyListingText.MaxTitleLength} characters; aim for {ListingSeoScorer.IdealTitleMin}-{ListingSeoScorer.IdealTitleMax}.");
        builder.AppendLine("- Start with the first keyword phrase, then a few readable descriptive words. It must read like a phrase, not a keyword list.");
        builder.AppendLine("- Do not use the same word more than twice. Use each of % : & + at most once.");
        builder.AppendLine("Tags (Etsy):");
        builder.AppendLine($"- Generate {TagCandidates} unique tag candidates, best first; the first {EtsyListingText.MaxTags} valid ones are kept.");
        builder.AppendLine($"- Every tag at most {EtsyListingText.MaxTagLength} characters, using only letters, numbers, spaces, hyphens and apostrophes.");
        builder.AppendLine("- Prefer multi-word phrases a buyer would type. Cover different angles: product type, style, theme, recipient, occasion.");
        builder.AppendLine("- Do not just repeat the title, and do not use the same word in more than four tags.");
        builder.AppendLine("- type is one of: primary, long_tail, niche, occasion, product_type.");
        builder.AppendLine("Description:");
        builder.AppendLine("- Open with a sentence that contains the first keyword phrase.");
        builder.AppendLine("- 120-250 words in short paragraphs separated by blank lines; natural, readable and buyer-focused.");
        builder.AppendLine($"- Use the keywords naturally: keyword words should stay at or below {ListingSeoScorer.MaxKeywordDensity}% of the description.");
        builder.AppendLine();
        builder.Append(ProductBlock(input));
        builder.AppendLine();
        builder.AppendLine(PolicyHeading);
        if (policyContext.Count == 0)
        {
            builder.AppendLine($"- {ListingContentPolicyDefaults.Title}: {ListingContentPolicyDefaults.Content}");
        }
        else
            foreach (var item in policyContext)
                builder.AppendLine($"- {item.Title}: {item.Content}");

        builder.AppendLine();
        builder.AppendLine("RELEVANT LISTING REFERENCES:");
        if (listingContext.Count == 0)
        {
            builder.AppendLine("No relevant listing reference was found. Follow the product data and policy standards.");
        }
        else
        {
            builder.AppendLine("Use these references only for tone, structure, and relevant keyword ideas. Do not copy them or treat their product facts as facts about the current product.");
            foreach (var item in listingContext)
                builder.AppendLine($"- {item.Title}: {item.Content}");
        }

        return builder.ToString();
    }

    private const string ProductHeading = "PRODUCT:";
    private const string PolicyHeading = "POLICY AND MARKETPLACE STANDARDS:";
    private const string DesignTextLabel = "Text printed on the design:";
    private const string ArtworkLabel = "Artwork:";

    /// <summary>The product part of the prompt: everything the listing is written from, and nothing that varies run to run.</summary>
    public static string ProductBlock(ListingContentInput input)
    {
        var builder = new StringBuilder();
        builder.AppendLine(ProductHeading);
        builder.AppendLine($"Name: {input.Name}");
        builder.AppendLine($"Product type: {input.ProductType}");
        builder.AppendLine($"Niche: {input.Niche ?? "not specified"}");
        builder.AppendLine($"Target audience: {input.TargetAudience ?? "not specified"}");
        builder.AppendLine($"Description: {input.Description}");
        builder.AppendLine($"Keywords: {string.Join(", ", input.Keywords)}");
        builder.AppendLine($"Notes: {input.Notes ?? "none"}");
        builder.AppendLine($"{DesignTextLabel} {input.DesignText ?? "none"}");
        builder.AppendLine($"{ArtworkLabel} {input.DesignPrompt ?? "not specified"}");
        return builder.ToString();
    }

    /// <summary>
    /// Whether <paramref name="promptUsed"/>, the prompt an earlier listing was written from, describes the product as
    /// <paramref name="input"/> does now. A prompt from before the design lines existed is compared without them, so an
    /// older listing still counts as current while the product itself is unchanged.
    /// </summary>
    public static bool DescribesSameProduct(string promptUsed, ListingContentInput input)
    {
        var stored = StoredProductBlock(promptUsed);
        if (stored is null) return false;
        var current = Lines(ProductBlock(input));
        if (!stored.Any(line => line.StartsWith(DesignTextLabel, StringComparison.Ordinal)))
            current = current.Where(line => !line.StartsWith(DesignTextLabel, StringComparison.Ordinal) && !line.StartsWith(ArtworkLabel, StringComparison.Ordinal)).ToArray();
        return stored.SequenceEqual(current, StringComparer.Ordinal);
    }

    private static string[]? StoredProductBlock(string promptUsed)
    {
        var text = promptUsed.Replace("\r\n", "\n");
        var start = text.IndexOf(ProductHeading + "\n", StringComparison.Ordinal);
        if (start < 0) return null;
        var end = text.IndexOf("\n\n" + PolicyHeading, start, StringComparison.Ordinal);
        return end < 0 ? null : Lines(text[start..(end + 1)]);
    }

    private static string[] Lines(string block) =>
        block.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd()).ToArray();
}
