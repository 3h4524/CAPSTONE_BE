-- style_art_presets: system-provided art styles for the image-generation select step.
-- Database-first: this script is the approved schema change; entity + context are
-- regenerated afterwards with ./scripts/Scaffold-Database.ps1 (or dotnet ef scaffold).

CREATE TABLE IF NOT EXISTS public.style_art_presets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NULL,
    name varchar NOT NULL,
    description text NOT NULL,
    style_modifiers text NOT NULL,
    preview_image_url text NULL,
    recommendations jsonb NOT NULL DEFAULT '[]'::jsonb,
    is_system_template boolean NOT NULL DEFAULT true,
    is_active boolean NOT NULL DEFAULT true,
    usage_count integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

-- Owner: null means a system preset, otherwise the seller who created it.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'style_art_presets_user_id_fkey'
    ) THEN
        ALTER TABLE public.style_art_presets
            ADD CONSTRAINT style_art_presets_user_id_fkey
            FOREIGN KEY (user_id) REFERENCES public.users (id) ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_style_art_presets_user_id
    ON public.style_art_presets (user_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_style_art_presets_name
    ON public.style_art_presets (lower(name));

CREATE INDEX IF NOT EXISTS idx_style_art_presets_is_active
    ON public.style_art_presets (is_active);

-- Seed: initial system presets (preview images supplied later by design).
INSERT INTO public.style_art_presets (user_id, name, description, style_modifiers, recommendations, is_system_template, is_active)
VALUES
    (NULL, 'Vintage',
     'Warm retro film look with nostalgic tones, suited for apparel and posters with a classic feel.',
     'vintage film photography, warm tones, subtle grain texture, soft retro lighting',
     '["Apparel graphics", "Retro posters", "Warm color palettes"]', true, true),
    (NULL, 'Minimalist',
     'Clean and airy composition with generous empty space, suited for modern brands and simple layouts.',
     'minimalist composition, clean lines, ample negative space, soft neutral tones',
     '["Modern brands", "Simple layouts", "Logo-centric designs"]', true, true),
    (NULL, 'Watercolor',
     'Soft painted look with fluid washes and gentle edges, suited for stationery and delicate artwork.',
     'watercolor painting, soft brush strokes, fluid color washes, delicate edges',
     '["Stationery", "Floral artwork", "Soft color stories"]', true, true),
    (NULL, 'Oil Painting',
     'Rich classical look with visible texture and dramatic light, suited for premium wall art.',
     'classical oil painting, rich impasto texture, dramatic chiaroscuro lighting, deep colors',
     '["Wall art", "Premium prints", "Dramatic subjects"]', true, true),
    (NULL, 'Anime',
     'Bold illustrated look with crisp lines and vivid colors, suited for stickers and youth apparel.',
     'anime illustration style, cel shading, vibrant colors, clean line art',
     '["Stickers", "Youth apparel", "Character artwork"]', true, true),
    (NULL, 'Photorealistic',
     'Lifelike photographic detail with natural light, suited for mockups and realistic product scenes.',
     'photorealistic, ultra detailed, natural lighting, sharp focus',
     '["Mockups", "Product scenes", "Realistic previews"]', true, true)
ON CONFLICT DO NOTHING;
