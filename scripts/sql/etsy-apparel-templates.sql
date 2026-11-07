BEGIN;

-- Apparel-focused system templates modeled on best-selling Etsy shirt styles.
-- Requires design-template-library.sql (art_style/system-owner constraints and negative_prompt column).
-- All three target light garments (white, ivory, sand, heather) and render on plain white for background removal.

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
    '70000000-0000-4000-8000-000000000010', NULL, 'Wildflower Line Bouquet', 'system',
    'Nature & Botanical', 'floral',
    'Create a print-ready {style} t-shirt graphic of {subject} for the {niche} niche, gathered into a loose hand-drawn bouquet. Add {keywords} as small filler sprigs. Use confident medium-weight ink outlines that stay visible when printed 10 inches wide, with soft flat color fills in a muted terracotta, sage, mustard, and dusty pink palette. Keep stems tied together at the bottom, a tall vertical silhouette, and generous space between blooms. Designed for light garments. Centered isolated artwork on a plain solid white background, no shadows, no product mockup.',
    'photorealistic flowers, watercolor bleed, gradient background, fading edges, hairline strokes, dense overlapping petals, text, t-shirt, mockup, fabric texture, checkerboard pattern, drop shadow, cropped subject, frame border, logo, watermark, signature',
    '[{"subject":"daisies, poppies, and lavender","prompt":"A tall loose bouquet of daisies, red poppies, and lavender sprigs tied at the stems, flat muted fills with clear ink outlines."},{"subject":"California wildflowers","prompt":"California poppies, lupine, and baby blue eyes gathered into a narrow bouquet with small grass sprigs."},{"subject":"cosmos and queen anne''s lace","prompt":"Airy cosmos blooms and queen anne''s lace in a balanced bouquet with a few curling leaves."}]'::jsonb,
    'Hand-drawn wildflower bouquets with flat muted fills for cottagecore and botanical shirts.',
    NULL, true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
),
(
    '70000000-0000-4000-8000-000000000011', NULL, 'Game Day Mom', 'system',
    'Sports & Fitness', 'bold_typography',
    'Create a print-ready {style} t-shirt graphic using the exact phrase "{subject}" for the {niche} niche. Preserve spelling and capitalization exactly and render every letter once. Stack the words in two or three lines of heavy condensed varsity lettering with a slight upward arch on the top line, and integrate one matching sports ball or equipment icon beside or inside the lettering. Use {keywords} as small stitch lines, stars, or speed marks. Limit the artwork to two solid inks plus white outlines, high contrast, slightly distressed print texture. Designed for light garments. Centered isolated artwork on a plain solid white background, no product mockup.',
    'misspelled text, extra letters, duplicated words, long sentence, thin script, tiny lettering, team names, league logos, player numbers, photorealistic ball, gradients, t-shirt, mockup, fabric texture, checkerboard pattern, drop shadow, cropped text, frame border, watermark, signature',
    '[{"subject":"BASEBALL MOM","prompt":"BASEBALL arched over a bold MOM, a baseball with red stitching tucked beside the M, navy and red inks."},{"subject":"SOCCER MAMA","prompt":"SOCCER stacked above MAMA in condensed varsity letters with a small soccer ball and two speed marks, black and kelly green inks."},{"subject":"GAME DAY VIBES","prompt":"GAME DAY arched over VIBES with a football icon and small stars, brown and burnt orange inks."}]'::jsonb,
    'Stacked varsity lettering with one sport icon for team-parent and game-day shirts.',
    NULL, true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
),
(
    '70000000-0000-4000-8000-000000000012', NULL, 'One-Line Pet Portrait', 'system',
    'Pets & Animals', 'minimalist',
    'Create a print-ready {style} t-shirt graphic of {subject} for the {niche} niche, drawn as a continuous single-line portrait. Capture the breed through head shape, ears, and muzzle with as few strokes as possible, and add {keywords} as one small accent such as a heart, paw print, or collar tag. Use one uniform black line of medium weight that stays visible when printed 8 inches wide, with an optional single flat color spot behind the head. Keep the face recognizable, the composition compact, and plenty of negative space. Designed for light garments. Centered isolated artwork on a plain solid white background, no product mockup.',
    'photorealistic fur, shading, cross-hatching, multiple line weights, sketchy broken lines, full body scene, busy background, text, t-shirt, mockup, fabric texture, checkerboard pattern, drop shadow, cropped ears, frame border, logo, watermark, signature',
    '[{"subject":"a golden retriever head","prompt":"A one-line golden retriever head with floppy ears and a small heart where the line ends, one soft mustard spot behind it."},{"subject":"a sitting tabby cat","prompt":"A continuous-line tabby cat sitting with its tail curled around its paws and a tiny paw print accent."},{"subject":"a french bulldog face","prompt":"A one-line french bulldog face with bat ears and a small collar tag, one dusty pink spot behind the head."}]'::jsonb,
    'Minimal continuous-line pet portraits for dog mom, cat lady, and breed-lover shirts.',
    NULL, true, true, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL
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
    is_system_template = EXCLUDED.is_system_template,
    is_active = EXCLUDED.is_active,
    updated_at = CURRENT_TIMESTAMP,
    deleted_at = NULL;

COMMIT;
