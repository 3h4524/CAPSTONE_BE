-- Apply once to databases created before Ideogram could generate design images.
BEGIN;
ALTER TABLE public.api_usage_records DROP CONSTRAINT IF EXISTS chk_api_usage_provider;
ALTER TABLE public.api_usage_records ADD CONSTRAINT chk_api_usage_provider
    CHECK (provider IN ('leonardo', 'stable_diffusion', 'openai', 'gemini', 'printify', 'etsy', 'ffmpeg_local', 'ideogram'));
COMMIT;
