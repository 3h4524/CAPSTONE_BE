using System.Text;
using APCS.Application.Features.ListingContent.Common;
using APCS.Application.Features.Rag.Dtos;

namespace APCS.Application.Features.ListingContent;

public static class ListingPromptBuilder
{
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
        builder.AppendLine("- Generate exactly 13 unique, concise tags.");
        builder.AppendLine("- For Etsy, keep every tag at 20 characters or fewer.");
        builder.AppendLine("- Do not invent product features, materials, sizes, or claims.");
        builder.AppendLine("- Make the title clear and keyword-rich without keyword stuffing.");
        builder.AppendLine("- Make the description natural, readable, and buyer-focused.");
        builder.AppendLine("- Use the reference context only as guidance; do not copy it verbatim.");
        builder.AppendLine();
        builder.AppendLine("PRODUCT:");
        builder.AppendLine($"Name: {input.Name}");
        builder.AppendLine($"Product type: {input.ProductType}");
        builder.AppendLine($"Niche: {input.Niche ?? "not specified"}");
        builder.AppendLine($"Target audience: {input.TargetAudience ?? "not specified"}");
        builder.AppendLine($"Description: {input.Description}");
        builder.AppendLine($"Keywords: {string.Join(", ", input.Keywords)}");
        builder.AppendLine($"Notes: {input.Notes ?? "none"}");
        builder.AppendLine();
        builder.AppendLine("POLICY AND MARKETPLACE STANDARDS:");
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
}
