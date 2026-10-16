-- Mock-up backgrounds: a job may put a template's garment on a plain color instead of the photo's background.
-- Database-first: run on Neon, then regenerate entities with ./scripts/Scaffold-Database.ps1.
-- The choice itself is kept in the job's config (mockupTemplateBackgrounds); the cut mask the garment is
-- lifted with lives in Cloudinary next to the other helper images (mockup-templates/{id}-{version}-cut).

-- '#RRGGBB' when the mock-up was rendered on a replaced background, NULL for the photo's own background.
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS background_color varchar(7) NULL;
