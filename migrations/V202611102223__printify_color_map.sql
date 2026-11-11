-- Maps a mock-up garment color (mockup_images.garment_color, '#RRGGBB') to the color name Printify uses, so Push to
-- Printify can order the colors the listing's mock-ups show. A row with no blueprint applies to every blueprint; a
-- row for one blueprint wins there, since providers name the same shade differently ("Black", "Solid Black").
CREATE TABLE IF NOT EXISTS public.printify_color_map (
    id uuid DEFAULT gen_random_uuid() PRIMARY KEY,
    hex character(7) NOT NULL CHECK (hex ~ '^#[0-9A-F]{6}$'),
    printify_name text NOT NULL CHECK (btrim(printify_name) <> ''),
    blueprint_id bigint NULL CHECK (blueprint_id > 0),
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- One name per color for all blueprints, and at most one override per blueprint.
CREATE UNIQUE INDEX IF NOT EXISTS uq_printify_color_map_shared
    ON public.printify_color_map (hex) WHERE blueprint_id IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_printify_color_map_blueprint
    ON public.printify_color_map (hex, blueprint_id) WHERE blueprint_id IS NOT NULL;
