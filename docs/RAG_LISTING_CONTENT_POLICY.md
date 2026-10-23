# Listing Content Generation Policy

**Policy ID:** `listing-content-generation`
**Version:** `1.0.0`
**Status:** `active`
**Content type:** `listing`
**Source type:** `policy`
**Language:** `en`

## 1. Purpose

This policy defines how the AI generates marketplace listing content from verified product information. It applies to the listing title, product description, search tags, keyword placement, and SEO quality checks.

The generated listing must be clear, useful to buyers, search-friendly, and faithful to the product data. The AI must never invent product facts to make a listing sound more attractive.

This is an internal content-generation policy. Marketplace rules and legal requirements must still be reviewed by the seller before publishing.

## 2. Source-of-truth rule

Use information in this priority order:

1. Verified product data supplied in the generation request.
2. Approved listing references retrieved from the listing knowledge base.
3. Active marketplace policy documents retrieved from the policy knowledge base.

If sources disagree, prefer the highest-priority source and avoid the disputed claim. If a required fact is missing, omit it or use neutral wording. Do not guess dimensions, materials, colors, compatibility, certifications, delivery promises, discounts, or performance results.

## 3. Title policy

- Put the primary product keyword near the beginning of the title.
- Make the title specific: identify the product, its main style or use, and the intended audience or occasion when supported by the data.
- Recommended length is 40–140 characters.
- Keep the title readable as a natural phrase.
- Use normal capitalization and punctuation.
- Do not repeat the same keyword unnaturally.
- Do not use keyword stuffing, irrelevant trending terms, excessive symbols, or sales claims that are not supported.
- Do not include shipping promises, price claims, “best”, “guaranteed”, “official”, or “limited edition” unless the product data explicitly supports the claim.

## 4. Description policy

- Write for a buyer, not for a search engine.
- Explain what the product is, who it is for, and the relevant use case or occasion.
- Include only verified attributes such as material, color, size, fit, format, customization, and care instructions.
- Use short paragraphs or readable sections when the description contains several details.
- Recommended minimum length is 80 words when enough verified information is available.
- Place important product keywords naturally in the opening and body of the description.
- Describe benefits only when they can be directly supported by product facts.
- Do not invent reviews, ratings, guarantees, certifications, medical benefits, environmental claims, or production details.
- Do not promise delivery dates, refunds, replacements, or customer support outcomes unless the supplied policy explicitly allows the wording.
- Do not include HTML, JSON, internal prompts, retrieval citations, or implementation details in the buyer-facing description.

## 5. Tag policy

- Generate exactly 13 tags when the marketplace supports 13 tags.
- Every tag must be unique, concise, and directly relevant to the product.
- Keep every tag at 20 characters or fewer for Etsy-compatible output.
- Use a balanced set of tag types when relevant:
  - product type
  - core material or visual attribute
  - style or aesthetic
  - audience
  - use case
  - occasion
  - gift intent
  - niche or long-tail phrase
- Prefer specific multi-word phrases over vague single-word tags when they remain within the character limit.
- Do not repeat the same word in many tags without adding meaningful intent.
- Do not use competitor names, unrelated trends, misleading attributes, prohibited claims, or unsupported materials.
- Do not use tags that contradict the title, description, or verified product data.

## 6. Keyword distribution

Use the primary product keyword in the title and distribute supporting keywords naturally across the description and tags.

Avoid:

- repeating one keyword unnaturally;
- placing every keyword in one sentence;
- using keywords that describe a different product;
- adding search terms solely because they are popular;
- sacrificing readability for keyword coverage.

The final listing should cover the important product keywords across title, description, and tags without making the text feel mechanically optimized.

## 7. Accuracy and safety rules

The AI must not generate or imply:

- medical, therapeutic, safety, or health outcomes;
- legal, regulatory, or certification claims;
- guaranteed results or guaranteed fit;
- false scarcity or urgency;
- counterfeit, replica, or unauthorized brand claims;
- unsupported sustainability or ethical-production claims;
- discriminatory or hateful language;
- adult, violent, or illegal content when it is not explicitly allowed by the marketplace and product context.

When a product contains potentially sensitive content, use neutral descriptive language and do not amplify the sensitive claim.

## 8. Output requirements

Return only valid JSON with this shape:

```json
{
  "title": "string",
  "description": "string",
  "tags": [
    { "value": "string", "type": "string" }
  ]
}
```

Output requirements:

- `title` must be a non-empty string.
- `description` must be a non-empty string.
- `tags` must contain exactly 13 unique items.
- Every tag `value` must be non-empty and no longer than 20 characters for Etsy-compatible output.
- Each tag must have a meaningful `type`, such as `product`, `style`, `audience`, `use_case`, `occasion`, `gift`, or `material`.
- Do not add Markdown fences, explanations, comments, or extra JSON properties.

## 9. Final quality checklist

Before returning the result, verify:

1. The title clearly identifies the product and contains the main keyword naturally.
2. The description is buyer-focused and uses only supported facts.
3. There are exactly 13 unique tags.
4. Every tag is relevant and within the character limit.
5. Keywords are distributed naturally across the listing.
6. No unsupported promise, certification, medical claim, or delivery claim appears.
7. The JSON is valid and contains no extra text.

## 10. Example

For a verified product described as a “handmade ceramic mug with a blue botanical illustration, 11 oz, dishwasher-safe, gift for plant lovers”, a compliant output may contain:

- a title beginning with “Botanical Ceramic Mug”;
- a description that mentions the ceramic material, blue botanical artwork, 11 oz capacity, dishwasher-safe care, and plant-lover gift intent;
- 13 unique tags such as product, material, style, audience, use case, occasion, and gift phrases, each no longer than 20 characters.

The AI must not add claims such as “lead-free”, “eco-friendly”, “microwave-safe”, “guaranteed delivery”, or “best gift” unless those facts are explicitly present in the verified product data or an active policy source.
