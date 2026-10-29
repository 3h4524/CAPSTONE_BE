BEGIN;

ALTER TABLE public.design_templates
    ADD COLUMN IF NOT EXISTS negative_prompt text;

UPDATE public.design_templates
SET is_system_template = false
WHERE is_system_template IS NULL;

UPDATE public.design_templates
SET is_active = true
WHERE is_active IS NULL;

UPDATE public.design_templates
SET usage_count = 0
WHERE usage_count IS NULL;

ALTER TABLE public.design_templates
    ALTER COLUMN is_system_template SET NOT NULL,
    ALTER COLUMN is_active SET NOT NULL,
    ALTER COLUMN usage_count SET NOT NULL;

ALTER TABLE public.design_templates
    DROP CONSTRAINT IF EXISTS chk_design_templates_type,
    DROP CONSTRAINT IF EXISTS chk_design_templates_art_style,
    DROP CONSTRAINT IF EXISTS chk_design_templates_system_owner,
    DROP CONSTRAINT IF EXISTS chk_design_templates_base_prompt_length,
    DROP CONSTRAINT IF EXISTS chk_design_templates_example_prompts_array;

ALTER TABLE public.design_templates
    ADD CONSTRAINT chk_design_templates_type CHECK (type IN ('system', 'personal')),
    ADD CONSTRAINT chk_design_templates_art_style CHECK (art_style IN ('vintage', 'minimalist', 'watercolor', 'bold_typography', 'dark_academia', 'funny_quote', 'floral')),
    ADD CONSTRAINT chk_design_templates_system_owner CHECK ((user_id IS NULL) = is_system_template),
    ADD CONSTRAINT chk_design_templates_base_prompt_length CHECK (char_length(base_prompt) BETWEEN 1 AND 1000),
    ADD CONSTRAINT chk_design_templates_example_prompts_array CHECK (jsonb_typeof(example_prompts) = 'array');

CREATE UNIQUE INDEX IF NOT EXISTS ux_design_templates_personal_name_active
    ON public.design_templates (user_id, lower(btrim(name)))
    WHERE deleted_at IS NULL AND is_system_template = false;

INSERT INTO public.design_templates (
    id,
    user_id,
    name,
    type,
    niche_category,
    art_style,
    base_prompt,
    negative_prompt,
    example_prompts,
    style_description,
    preview_image_url,
    is_system_template,
    is_active,
    usage_count,
    created_at,
    updated_at,
    deleted_at)
VALUES
(
    '70000000-0000-4000-8000-000000000001', NULL, 'Moonlit Botanicals', 'system',
    'Nature & Botanical', 'dark_academia',
    'Create a centered, print-ready {style} illustration of {subject} for the {niche} niche. Frame the subject with {keywords}, lunar accents, and refined vintage linework. Use a near-black, bone, and muted botanical palette, a strong silhouette, and clean negative space. Isolated artwork, no product mockup.',
    'photorealism, glossy 3D render, busy background, muddy blacks, blurry edges, illegible text, logo, watermark, product mockup',
    '[{"subject":"luna moth with wildflowers","prompt":"A symmetrical luna moth framed by yarrow, fern, and small lunar phases, rendered in bone ink on deep black."},{"subject":"raven among poisonous herbs","prompt":"A centered raven above belladonna and foxglove, enclosed by a restrained moon-phase arch."}]'::jsonb,
    'Detailed moth and wildflower compositions for apparel and wall art.',
    '/images/design-templates/moonlit-botanicals.webp', true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
),
(
    '70000000-0000-4000-8000-000000000002', NULL, 'Desert Horizon', 'system',
    'Outdoors', 'vintage',
    'Create a print-ready {style} desert scene featuring {subject} for the {niche} niche. Weave in {keywords} as secondary motifs. Use a limited sun-faded palette, layered silhouettes, rugged halftone texture, and large screen-print-friendly shapes. Centered isolated artwork, no product mockup.',
    'neon colors, glossy 3D, photorealistic landscape, thin unreadable details, crowded composition, logo, watermark, product mockup',
    '[{"subject":"sunset over a winding canyon trail","prompt":"A burnt-orange sun setting behind layered canyon silhouettes with a single winding trail."},{"subject":"saguaro beneath a crescent moon","prompt":"A tall saguaro and distant mesas framed by a faded crescent moon and sparse desert stars."}]'::jsonb,
    'Warm retro landscapes with strong silhouettes for outdoor collections.',
    '/images/design-templates/desert-horizon.webp', true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
),
(
    '70000000-0000-4000-8000-000000000003', NULL, 'Coffee Geometry', 'system',
    'Food & Drink', 'minimalist',
    'Create a print-ready {style} composition of {subject} for the {niche} niche. Reduce {keywords} into simple geometric accents. Use clear negative space, balanced circles and lines, and a two-color high-contrast palette. Centered isolated artwork, no product mockup.',
    'busy background, photorealism, gradients, excessive detail, weak contrast, logo, watermark, product mockup',
    '[{"subject":"a steaming coffee cup","prompt":"A simple cup reduced to circles and arcs, with one calm ribbon of steam."},{"subject":"a moka pot and coffee beans","prompt":"A centered moka pot built from clean geometric planes with three bean-shaped accents."}]'::jsonb,
    'Clean geometric compositions for mugs, kitchen prints, and cafe gifts.',
    '/images/design-templates/coffee-geometry.webp', true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
),
(
    '70000000-0000-4000-8000-000000000004', NULL, 'Campfire Crest', 'system',
    'Outdoors', 'vintage',
    'Design a compact, print-ready {style} outdoor badge featuring {subject} for the {niche} niche. Integrate {keywords} around a clear central silhouette, rugged ink texture, and a limited heritage palette. Keep every shape legible at small sizes. Isolated emblem, no product mockup.',
    'photograph, complex scenery, tiny text, thin outlines, glossy effects, logo, watermark, product mockup',
    '[{"subject":"mountain campsite at dawn","prompt":"A mountain, two pines, a tent, and a campfire arranged inside a circular badge."},{"subject":"canoe beside a pine lake","prompt":"A canoe crossing still water beneath a compact pine-and-mountain crest."}]'::jsonb,
    'Badge-style camping artwork made for tees, stickers, and patches.',
    '/images/design-templates/campfire-crest.webp', true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
),
(
    '70000000-0000-4000-8000-000000000005', NULL, 'Blue Garden', 'system',
    'Nature & Botanical', 'watercolor',
    'Paint a print-ready {style} arrangement of {subject} for the {niche} niche. Blend {keywords} into airy stems and natural asymmetry, using translucent washes and a cool blue-green palette while keeping the central subject clear. Isolated artwork on a clean background, no product mockup.',
    'hard vector outlines, neon saturation, muddy washes, dense background, readable text, logo, watermark, product mockup',
    '[{"subject":"anemones and hydrangeas","prompt":"A loose bouquet of blue hydrangeas, white anemones, and sage leaves with open breathing space."},{"subject":"blue iris and eucalyptus","prompt":"Three blue irises balanced by curved eucalyptus stems and soft transparent shadows."}]'::jsonb,
    'Soft floral arrangements for calm home, stationery, and wellness products.',
    '/images/design-templates/blue-garden.webp', true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
),
(
    '70000000-0000-4000-8000-000000000006', NULL, 'Heritage Motorcycle', 'system',
    'Vehicles', 'vintage',
    'Create a print-ready {style} engraving of {subject} for the {niche} niche. Use {keywords} only as restrained mechanical accents, with confident black linework, crisp metal highlights, controlled cross-hatching, and no background clutter. Centered isolated artwork, no product mockup.',
    'modern sport bike, color photograph, motion blur, distorted wheels, incorrect engine geometry, brand logo, watermark, product mockup',
    '[{"subject":"classic cafe racer motorcycle","prompt":"A three-quarter cafe racer with accurate wheels, tank, and detailed engine linework."},{"subject":"vintage scrambler motorcycle","prompt":"A centered scrambler with engraved metal detail, high exhaust pipes, and restrained ground shadow."}]'::jsonb,
    'Engraved mechanical illustrations for garage and rider collections.',
    '/images/design-templates/heritage-motorcycle.webp', true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
),
(
    '70000000-0000-4000-8000-000000000007', NULL, 'Bold Rally', 'system',
    'Quotes & Motivation', 'bold_typography',
    'Create a print-ready {style} composition using the exact phrase "{subject}" for the {niche} niche. Preserve spelling and capitalization exactly. Build a dominant condensed sans-serif hierarchy, use {keywords} as small supporting motifs, and limit the artwork to two or three solid inks with strong alignment and generous negative space. Isolated artwork, no product mockup.',
    'misspelled words, extra words, distorted letters, thin script, low contrast, photographic background, logo, watermark, product mockup',
    '[{"subject":"KEEP MOVING FORWARD","prompt":"Stack the exact phrase in three compact lines with a forward arrow cutting through the final word."},{"subject":"MAKE YOUR OWN LUCK","prompt":"Use tall condensed lettering with a small four-leaf symbol and a strong centered baseline."}]'::jsonb,
    'High-impact exact-text compositions for motivational apparel and posters.',
    '/images/design-templates/bold-rally.webp', true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
),
(
    '70000000-0000-4000-8000-000000000008', NULL, 'Dry Humor Club', 'system',
    'Humor & Lifestyle', 'funny_quote',
    'Create a print-ready {style} merch graphic built around the exact phrase "{subject}" for the {niche} niche. Preserve spelling and capitalization exactly. Pair the quote with one simple deadpan illustration inspired by {keywords}; use chunky readable lettering, two or three flat inks, and generous negative space. Centered isolated artwork, no product mockup.',
    'long paragraph, misspelled text, offensive imagery, crowded scene, realistic photograph, tiny lettering, logo, watermark, product mockup',
    '[{"subject":"RUNNING ON COFFEE AND BAD IDEAS","prompt":"Pair the exact quote with a tired coffee cup and a nearly empty battery icon."},{"subject":"MY WEEKEND IS FULLY BOOKED","prompt":"Place the exact quote around a deadpan cat sitting on a short stack of books."}]'::jsonb,
    'Short deadpan quotes paired with simple, readable character art.',
    '/images/design-templates/dry-humor-club.webp', true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
),
(
    '70000000-0000-4000-8000-000000000009', NULL, 'Birth Month Blooms', 'system',
    'Gifts & Occasions', 'floral',
    'Create a print-ready {style} composition of {subject} for the {niche} niche. Weave in {keywords} as small botanical details. Use graceful stems, balanced bouquet or wreath spacing, clean outlines, and a limited natural palette. Centered isolated artwork, no product mockup.',
    'photorealistic flower photo, plastic texture, cluttered background, heavy shadows, neon palette, text, logo, watermark, product mockup',
    '[{"subject":"January carnation and snowdrop bouquet","prompt":"A balanced bouquet of carnations and snowdrops with curved stems and small winter leaves."},{"subject":"June rose and honeysuckle wreath","prompt":"An open circular wreath of roses and honeysuckle with a clean center for optional personalization."}]'::jsonb,
    'Gift-ready floral structures for birth-month and occasion collections.',
    '/images/design-templates/birth-month-blooms.webp', true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
)
ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    type = EXCLUDED.type,
    niche_category = EXCLUDED.niche_category,
    art_style = EXCLUDED.art_style,
    base_prompt = EXCLUDED.base_prompt,
    negative_prompt = EXCLUDED.negative_prompt,
    example_prompts = EXCLUDED.example_prompts,
    style_description = EXCLUDED.style_description,
    preview_image_url = EXCLUDED.preview_image_url,
    is_system_template = EXCLUDED.is_system_template,
    is_active = EXCLUDED.is_active,
    updated_at = CURRENT_TIMESTAMP,
    deleted_at = NULL;

COMMIT;
