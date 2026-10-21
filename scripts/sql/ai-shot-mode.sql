-- Apply once to databases created before AI Shot was enabled.
BEGIN;
ALTER TABLE public.promo_videos DROP CONSTRAINT IF EXISTS promo_videos_mode_check;
ALTER TABLE public.promo_videos DROP CONSTRAINT IF EXISTS chk_promo_videos_mode;
ALTER TABLE public.promo_videos ADD CONSTRAINT chk_promo_videos_mode CHECK (mode IN ('standard', 'ai_shot'));
COMMIT;
