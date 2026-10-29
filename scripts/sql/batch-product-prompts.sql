-- batch_product_prompts: seller prompt overrides stored per batch row.
-- Database-first: this script is the approved schema change; entity + context are
-- updated manually to match the scaffold output.
-- Null means "use the default synthesized prompt" for that field.

ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_subject text NULL;

ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_art_style text NULL;

ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_mood_tone text NULL;

ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_negative_terms text NULL;

ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_instructions text NULL;
