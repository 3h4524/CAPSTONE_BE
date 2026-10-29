-- mockup_templates: system-provided mock-up templates for the batch setup picker.
-- Database-first: this script is the approved schema change (owner column, indexes)
-- plus seed data; entity + context are regenerated afterwards with
-- ./scripts/Scaffold-Database.ps1 (or dotnet ef scaffold).
-- Owner: null means a system template, otherwise the seller who created it.

ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS user_id uuid NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'mockup_templates_user_id_fkey'
    ) THEN
        ALTER TABLE public.mockup_templates
            ADD CONSTRAINT mockup_templates_user_id_fkey
            FOREIGN KEY (user_id) REFERENCES public.users (id) ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_mockup_templates_user_id
    ON public.mockup_templates (user_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_mockup_templates_name
    ON public.mockup_templates (lower(name));

INSERT INTO public.mockup_templates
    (name, product_type, base_image_url, preview_image_url, print_area_config,
     output_width_px, output_height_px, is_system_template, is_active, usage_count)
VALUES
    ('Classic Tee - Male Model', 'tshirt',
     'https://cdn.example.com/mockups/tshirt-male-model.jpg', NULL,
     '{"x": 820, "y": 640, "width": 900, "height": 1100, "unit": "px"}',
     2000, 2000, true, true, 0),
    ('Classic Tee - Flat Lay', 'tshirt',
     'https://cdn.example.com/mockups/tshirt-flat-lay.jpg', NULL,
     '{"x": 750, "y": 700, "width": 900, "height": 1100, "unit": "px"}',
     2000, 2000, true, true, 0),
    ('Pullover Hoodie - Female Model', 'hoodie',
     'https://cdn.example.com/mockups/hoodie-female-model.jpg', NULL,
     '{"x": 800, "y": 700, "width": 950, "height": 1050, "unit": "px"}',
     2000, 2000, true, true, 0),
    ('Pullover Hoodie - Hanger', 'hoodie',
     'https://cdn.example.com/mockups/hoodie-hanger.jpg', NULL,
     '{"x": 780, "y": 720, "width": 950, "height": 1050, "unit": "px"}',
     2000, 2000, true, true, 0),
    ('Ceramic Mug - Front View', 'mug',
     'https://cdn.example.com/mockups/mug-front.jpg', NULL,
     '{"x": 900, "y": 800, "width": 500, "height": 400, "unit": "px"}',
     2000, 2000, true, true, 0),
    ('Ceramic Mug - Lifestyle Desk', 'mug',
     'https://cdn.example.com/mockups/mug-lifestyle.jpg', NULL,
     '{"x": 1050, "y": 750, "width": 480, "height": 380, "unit": "px"}',
     2000, 2000, true, true, 0),
    ('Framed Poster - Wall Scene', 'poster',
     'https://cdn.example.com/mockups/poster-wall.jpg', NULL,
     '{"x": 600, "y": 400, "width": 1800, "height": 2400, "unit": "px"}',
     3000, 3000, true, true, 0),
    ('Framed Poster - Close Up', 'poster',
     'https://cdn.example.com/mockups/poster-closeup.jpg', NULL,
     '{"x": 500, "y": 350, "width": 2000, "height": 2600, "unit": "px"}',
     3000, 3000, true, true, 0),
    ('Phone Case - Handheld', 'phone_case',
     'https://cdn.example.com/mockups/phone-case-handheld.jpg', NULL,
     '{"x": 850, "y": 500, "width": 450, "height": 900, "unit": "px"}',
     2000, 2000, true, true, 0),
    ('Phone Case - Flat Lay', 'phone_case',
     'https://cdn.example.com/mockups/phone-case-flat.jpg', NULL,
     '{"x": 780, "y": 560, "width": 450, "height": 900, "unit": "px"}',
     2000, 2000, true, true, 0),
    ('Canvas Tote - Female Model', 'tote_bag',
     'https://cdn.example.com/mockups/tote-female-model.jpg', NULL,
     '{"x": 820, "y": 900, "width": 700, "height": 750, "unit": "px"}',
     2000, 2000, true, true, 0),
    ('Canvas Tote - Hanging', 'tote_bag',
     'https://cdn.example.com/mockups/tote-hanging.jpg', NULL,
     '{"x": 800, "y": 850, "width": 700, "height": 750, "unit": "px"}',
     2000, 2000, true, true, 0)
ON CONFLICT DO NOTHING;
