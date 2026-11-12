-- style_art_presets: system-provided art styles for the image-generation select step.
-- Database-first: this script is the approved schema change; entity + context are
-- regenerated afterwards with ./scripts/Scaffold-Database.ps1 (or dotnet ef scaffold).

CREATE TABLE IF NOT EXISTS public.style_art_presets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    -- Owner: null means a system preset, otherwise the seller who created it.
    user_id uuid NULL REFERENCES public.users (id) ON DELETE CASCADE,
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

CREATE INDEX IF NOT EXISTS idx_style_art_presets_user_id
    ON public.style_art_presets (user_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_style_art_presets_name
    ON public.style_art_presets (lower(name));

CREATE INDEX IF NOT EXISTS idx_style_art_presets_is_active
    ON public.style_art_presets (is_active);

-- Seed: 12 system presets tailored for Print-on-Demand merchandise with hosted preview images.
INSERT INTO public.style_art_presets (user_id, name, description, style_modifiers, preview_image_url, recommendations, is_system_template, is_active)
VALUES
    (
        NULL,
        'Vintage Distressed Badge',
        'Classic American heritage emblem with weathered screen print texture. Ideal for masculine tees, biker gear, and garage motifs.',
        'vintage typography badge emblem, distressed weathered texture, retro screen print, limited color palette, apparel graphic tee design, clean silhouette',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794492661/apcs/style-presets/vintage_badge.jpg',
        '["Apparel graphics", "Vintage t-shirts", "Biker & garage merch"]'::jsonb,
        true,
        true
    ),
    (
        NULL,
        'Cyberpunk Neon',
        'High-tech futuristic streetwear aesthetic with glowing magenta, cyan accents and sharp geometric cybernetics.',
        'cyberpunk aesthetic, vibrant neon glow, futuristic tech elements, dark synthwave palette, high contrast graphic illustration, streetwear merchandise',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794492665/apcs/style-presets/cyberpunk_neon.jpg',
        '["Streetwear tees", "Gaming merch", "Dark apparel"]'::jsonb,
        true,
        true
    ),
    (
        NULL,
        'Retro 80s Synthwave',
        'Nostalgic 1980s outrun aesthetic featuring glowing horizon grids, chrome reflections, and iconic neon sunsets.',
        '80s retrowave outrun aesthetics, vibrant synthwave gradient, wireframe grid, palm tree silhouettes, retro chrome style, bold graphic print',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794492673/apcs/style-presets/retro_synthwave.jpg',
        '["Retro apparel", "Graphic hoodies", "Summer posters"]'::jsonb,
        true,
        true
    ),
    (
        NULL,
        'Dark Gothic Tattoo',
        'Neo-traditional dark tattoo flash with bold black contours, ornate roses, and delicate stippling shading.',
        'neo-traditional tattoo flash illustration, dark gothic aesthetic, bold black ink linework, stippling dot shading, high contrast apparel art',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794492677/apcs/style-presets/gothic_tattoo.jpg',
        '["Rock band tees", "Dark streetwear", "Phone cases"]'::jsonb,
        true,
        true
    ),
    (
        NULL,
        'Kawaii Chibi Doodle',
        'Sweet, playful character art with thick rounded outlines and cheerful pastel colors. Best-seller for stickers and accessories.',
        'kawaii cute chibi doodle character, vibrant pastel colors, clean thick rounded outlines, playful sticker art, flat vector illustration, isolated graphics',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794492682/apcs/style-presets/kawaii_chibi.jpg',
        '["Die-cut stickers", "Coffee mugs", "Tote bags", "Kids apparel"]'::jsonb,
        true,
        true
    ),
    (
        NULL,
        'Anime Manga Clean',
        'Crisp cel-shaded Japanese anime illustration with razor-sharp line art and luminous vibrant coloring.',
        'modern Japanese anime illustration style, crisp cel-shaded clean line art, luminous vibrant colors, aesthetic graphic tee print',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794492688/apcs/style-presets/anime_clean.jpg',
        '["Anime merch", "Youth apparel", "Posters & prints"]'::jsonb,
        true,
        true
    ),
    (
        NULL,
        'Pixel Art 8-Bit',
        'Charming retro arcade aesthetic with sharp pixel grid definition and handcrafted 8-bit / 16-bit color palettes.',
        'nostalgic 8-bit pixel art style, isometric arcade aesthetic, vibrant limited pixel palette, sharp pixel grid details, retro gaming merch',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794492692/apcs/style-presets/pixel_art.jpg',
        '["Gamer t-shirts", "Enamel pin designs", "Mugs & coasters"]'::jsonb,
        true,
        true
    ),
    (
        NULL,
        'Minimalist Botanical',
        'Scandinavian continuous contour line art paired with organic earthy arches. Perfect for modern home decor and canvas art.',
        'minimalist boho line art, continuous fluid contour lines, warm terracotta and sage botanical silhouettes, modern Scandinavian aesthetic, elegant negative space',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794491857/apcs/style-presets/minimalist.jpg',
        '["Canvas wall art", "Tote bags", "Throw pillows", "Home decor"]'::jsonb,
        true,
        true
    ),
    (
        NULL,
        'Watercolor Florals',
        'Delicate wet-on-wet paint washes with blooming floral edges on textured cotton paper. Ideal for gifts and cards.',
        'delicate watercolor painting, fluid wet-on-wet brush washes, blooming botanical florals, soft pastel tones, textured cotton paper edges',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794491861/apcs/style-presets/watercolor.jpg',
        '["Stationery & cards", "Floral tote bags", "Light apparel"]'::jsonb,
        true,
        true
    ),
    (
        NULL,
        'Classical Oil Painting',
        'Museum-quality fine art with thick impasto brush texture, dramatic chiaroscuro contrast, and rich jewel tones.',
        'classical oil painting, rich impasto texture, dramatic chiaroscuro lighting, deep colors, museum quality artwork',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794491865/apcs/style-presets/oil_painting.jpg',
        '["Framed wall art", "Premium canvas prints", "Art collections"]'::jsonb,
        true,
        true
    ),
    (
        NULL,
        'Photorealistic Studio',
        'Lifelike commercial studio photography with soft natural morning rim light and crisp macro product detail.',
        'photorealistic ultra-detailed commercial product scene, crisp natural studio rim lighting, macro textures, shallow depth of field, sharp 8k focus',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794491875/apcs/style-presets/photorealistic.jpg',
        '["Lifestyle mockups", "Product preview scenes", "Catalog banners"]'::jsonb,
        true,
        true
    ),
    (
        NULL,
        'Vintage Retro Film',
        'Nostalgic 1970s analog 35mm film photograph with warm golden hour grain and faded kodachrome tones.',
        'vintage film photography, warm tones, subtle grain texture, soft retro lighting, 1970s kodachrome aesthetic',
        'https://res.cloudinary.com/v05yzogx/image/upload/v1794491774/apcs/style-presets/vintage.jpg',
        '["Vintage apparel", "Retro posters", "Warm aesthetic prints"]'::jsonb,
        true,
        true
    )
ON CONFLICT (lower(name)) DO UPDATE SET
    description = EXCLUDED.description,
    style_modifiers = EXCLUDED.style_modifiers,
    preview_image_url = EXCLUDED.preview_image_url,
    recommendations = EXCLUDED.recommendations,
    is_system_template = EXCLUDED.is_system_template,
    is_active = EXCLUDED.is_active,
    updated_at = now();
