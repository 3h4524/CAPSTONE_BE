-- Realistic mock-up compositing: print maps (shading + displacement) and garment recolor.
-- Database-first: run on Neon, then regenerate entities with ./scripts/Scaffold-Database.ps1.
-- The helper images live in Cloudinary under keys derived from the template id and version
-- (mockup-templates/{id}-{version}-displace, -mask), so only their freshness is stored here.

-- The base_image_url the helper images were generated from; they are used only while it still
-- equals base_image_url, so a replaced photo never composites with stale maps.
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS print_maps_source_url text NULL;

-- Set on every regeneration and part of the helper keys, so composite URLs change with the maps
-- instead of the CDN serving renders of the previous ones.
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS print_maps_version bigint NULL;

-- Whether the garment was separated cleanly and is light: designs are then multiplied onto it so
-- they take on the fabric's shading.
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS garment_is_light boolean NOT NULL DEFAULT false;

-- Set only after a garment mask was generated and the photo passed the recolor checks.
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS allow_recolor boolean NOT NULL DEFAULT false;

-- '#RRGGBB' the template renders its garment in by default (needs allow_recolor), NULL for the photo's own color.
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS garment_color varchar(7) NULL;

-- '#RRGGBB' when the mock-up was rendered with a recolored garment, NULL for the photo's own color.
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS garment_color varchar(7) NULL;

-- Whether the stored base photo is already the garment cut out onto a plain backdrop. Such a photo
-- is final: its helper images come from the cut itself and are not regenerated from it.
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS background_removed boolean NOT NULL DEFAULT false;
