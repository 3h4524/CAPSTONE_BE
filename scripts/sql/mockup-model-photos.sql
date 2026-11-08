-- Model photos: a mock-up template can be a photo of a person wearing a plain, light garment.
-- Database-first: run on Neon, then regenerate entities with ./scripts/Scaffold-Database.ps1.
-- A model photo's garment mask is the part of the person the seller selected (the shirt only), so the
-- design and the recolor never reach the skin, hair or other clothes. Its helper images live in
-- Cloudinary like any template's (mockup-templates/{id}-{version}-displace, -mask, -cut).

-- 'garment' for a product photo (the garment alone), 'model' for a person wearing the garment.
-- Model photos are never offered in a batch's mock-up selection; they dress an existing mock-up.
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS kind varchar(16) NOT NULL DEFAULT 'garment';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'mockup_templates_kind_check') THEN
        ALTER TABLE public.mockup_templates
            ADD CONSTRAINT mockup_templates_kind_check CHECK (kind IN ('garment', 'model'));
    END IF;
END $$;
