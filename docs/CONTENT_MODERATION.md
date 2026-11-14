# Content moderation (guardrail)

The platform checks seller content itself instead of relying only on each provider's safety filter. Gemini and Ideogram (`IsBlocked`), Veo (`raiMediaFilteredReasons`) and MiniMax (`ContentPolicyFailure`) still refuse content on their own; the guardrail runs before them on text and after them on images.

Phase 1 covers design generation, AI Studio keyframes and AI scene video. Uploaded images, video frames, model photos and lifestyle mock-ups are not checked yet.

## How a check runs

`IContentModerationService` (`Application/Features/Moderation/ContentModerationService.cs`) runs three steps.

1. **Blocklist.** Terms from `moderation_terms` are matched in process. Text is normalized first: case is folded, look-alike digits and symbols are replaced (`s3x`), and spaced-out letters are joined (`s.e.x`). A term matches whole words only (`sex` does not match `Sussex`), and its letters may repeat (`nudeee`). Phrases in `moderation_allowlist` are masked before matching. Vietnamese is matched with its accents. A term with `match_unaccented` is also matched once accents are removed; set it only when the accent-free spelling is not an everyday word ("lồn" without accents is "lon", a can).
   - A `block` term refuses the text at once, without asking Gemini.
   - A `flag` term is passed to Gemini as a hint.
2. **Gemini.** `Moderation:Model` (Gemini 3.5 Flash-Lite) runs on the platform's `Gemini:TextGeneration` key at temperature 0 and answers with a JSON schema. The seller's text is wrapped in `<content>` elements and escaped, and the model is told it is data, not instructions. Images are sent inline.
3. **Policy.** `ModerationPolicy` turns the model's ratings into a decision, so thresholds can change without changing the prompt. Each category's threshold is stored in `moderation_settings` (`block_at.<category>`) and set on the admin page:

| Rating | Decision |
|---|---|
| `sexual_minors`, any severity | Block (fixed, no setting) |
| at or above the category's threshold (`high` by default, or `medium`) | Block |
| `medium` below the threshold | Flagged: let through and recorded |
| `low` or nothing found | Allow |

Categories: `sexual`, `sexual_minors`, `violence_gore`, `hate`, `self_harm`, `illegal`, `ip_trademark`.

If Gemini's own safety filter refuses the classification request, the provider raises `GeminiContentBlockedException` and the content is blocked.

Text verdicts are cached for `Moderation:CacheMinutes`, keyed by the policy version, a fingerprint of the blocklist and allowlist, a fingerprint of the settings, and a SHA-256 of the normalized text. A change on the admin page therefore applies to the next check without clearing the cache. Image verdicts are not cached.

## Where it runs

| Point | Text or image |
|---|---|
| Saving a product (add, edit, CSV import) | Name, niche, keywords, description, design text, custom prompt. The import runs the blocklist only: 500 rows would otherwise wait on 500 Gemini calls |
| Saving a row's prompt edits | Subject, art style, mood, instructions, custom prompt. Negative terms are not checked: they name what must not appear |
| Starting a job, regenerating a product | Job instructions; product instructions, custom prompt and design text |
| Saving a personal design template or art style | Name, art style or modifiers, base prompt, examples |
| Saving a workflow | AI scene directions: shared, per scene and per product |
| AI Studio keyframe quote | Custom keyframe prompt. Generating goes through the quote |
| Before a design job asks the provider for images | All of the seller's text behind the product's prompt, in full |
| After planning an AI video | Every AI shot prompt, after the Gemini rewrite |
| Before an AI scene is sent to MiniMax or Veo | That scene's prompt. A block fails the scene; nothing is sent or billed |
| After a design or keyframe image is generated | The image |

## When the check is unavailable

- **Input text:** let through and recorded as `unchecked`. The blocklist has already run.
- **Output image:** kept, but never approved automatically. Approving it later (`ModerationApproval.EnsureApprovableAsync`) checks it again. If the check is still unavailable, the approval is refused with `ModerationPending`; if the image is now blocked, with `ContentPolicyViolation`. Images made before the guardrail have no record and are not held back.
- **Gemini switched off** (`Moderation:Enabled=false`, the admin page's switch, or no Gemini key): only the blocklist runs, images are not checked, and nothing is recorded.

A blocked request returns HTTP 400 with code `ContentPolicyViolation` and a message naming the category and the field. Thresholds and scores are not shown to the seller.

## Data

The tables are created by `migrations/V202611132054__content_moderation.sql`.

- **`moderation_terms`, `moderation_allowlist`:** edited on the admin page. Read again every `Moderation:ListRefreshMinutes`, or at once after an edit.
- **`moderation_settings`:** the Gemini switch (`enabled`) and each category's threshold (`block_at.<category>`), edited on the admin page.
- **`moderation_checks`:** one row per evaluated decision. Cached verdicts are not written again. It is the audit trail and the source of statistics. For an output image, its latest row decides whether the image can be approved.
  - `content_excerpt` keeps the seller's text (up to 500 characters) only for a text check that was blocked, flagged or unchecked. Allowed text and images keep none. It is shown only on the admin page.
  - Rows are not pruned yet. A daily job that deletes `allow` rows after 90 days and the others after 365 days is planned.

Every edit of a term, an allowlist phrase or the settings is written to `audit_logs`.

`ModerationStore` writes on a database context of its own, so recording a check never saves, or rolls back, what the calling use case has staged. A database the migration has not reached yet reads as an empty blocklist and skips the log.

## Admin page

`/admin/content-safety` (Admin and Super Admin) has four tabs.

| Tab | What it shows |
|---|---|
| Overview | Totals, decisions per day, blocks by category, layer and feature, and the sellers blocked most often (7, 30 or 90 days) |
| Activity log | Every decision with filters; a check opens with its findings, its kept text with the evidence marked, and a shortcut to allowlist a false positive |
| Blocklist | Terms and allowlisted phrases: add, edit, pause, delete |
| Settings | The Gemini switch and each category's threshold |

Its API is `api/admin/moderation`: `overview`, `offenders`, `checks`, `checks/{id}`, `terms`, `allowlist` and `settings`. While the moderation tables do not exist, reads return `storageReady: false` with empty data, and writes return `409 ModerationStorageMissing`.

## Configuration

```json
"Moderation": {
  "Enabled": true,
  "Model": "gemini-3.5-flash-lite",
  "TimeoutSeconds": 10,
  "PolicyVersion": 1,
  "CacheMinutes": 1440,
  "ListRefreshMinutes": 10
}
```

Raise `PolicyVersion` whenever the prompt, the categories or `ModerationPolicy` changes, so verdicts cached under the old policy are not reused.
