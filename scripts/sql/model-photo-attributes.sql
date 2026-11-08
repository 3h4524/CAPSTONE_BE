-- Model photo attributes: who is in a model photo, so sellers can filter the library, and whether AI made it.
-- Database-first: run on Neon, then regenerate entities with ./scripts/Scaffold-Database.ps1.
-- Only model photos (kind = 'model') carry them; garment templates leave them null.
--   people     'single' (one person) or 'group' (several people, all dressed in the same mock-up)
--   age_group  for one person: 'child', 'teen', 'adult' or 'senior'
--   gender     for one person: 'female' or 'male'
--   group_type for a group: 'family', 'friends' or 'couple'
--   ai_generated  the photo was made by AI (Ideogram) rather than uploaded

ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS people varchar(8);
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS age_group varchar(8);
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS gender varchar(8);
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS group_type varchar(8);
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS ai_generated boolean NOT NULL DEFAULT false;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'mockup_templates_people_check') THEN
        ALTER TABLE public.mockup_templates
            ADD CONSTRAINT mockup_templates_people_check CHECK (people IS NULL OR people IN ('single', 'group'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'mockup_templates_age_group_check') THEN
        ALTER TABLE public.mockup_templates
            ADD CONSTRAINT mockup_templates_age_group_check CHECK (age_group IS NULL OR age_group IN ('child', 'teen', 'adult', 'senior'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'mockup_templates_gender_check') THEN
        ALTER TABLE public.mockup_templates
            ADD CONSTRAINT mockup_templates_gender_check CHECK (gender IS NULL OR gender IN ('female', 'male'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'mockup_templates_group_type_check') THEN
        ALTER TABLE public.mockup_templates
            ADD CONSTRAINT mockup_templates_group_type_check CHECK (group_type IS NULL OR group_type IN ('family', 'friends', 'couple'));
    END IF;
END $$;

-- Model photos saved before these attributes are of one person.
UPDATE public.mockup_templates SET people = 'single' WHERE kind = 'model' AND people IS NULL;
