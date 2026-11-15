-- A seller's Pinterest account, connected with OAuth so Push to media can post pins to one of their boards.
-- A seller has at most one connected account; disconnecting sets deleted_at and blanks the tokens.
CREATE TABLE IF NOT EXISTS public.pinterest_integrations (
    id uuid DEFAULT gen_random_uuid() PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    pinterest_username character varying(255) NOT NULL,
    access_token_encrypted text NOT NULL,
    refresh_token_encrypted text,
    access_expires_at timestamp with time zone,
    refresh_expires_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_pinterest_integrations_user
    ON public.pinterest_integrations (user_id) WHERE deleted_at IS NULL;

-- A pin is made from a mock-up, not from the promo video, so a share no longer needs a video. The mock-up and the
-- board (destination_id) it went to let a retried step skip the pins it already posted.
ALTER TABLE public.social_media_shares ALTER COLUMN promo_video_id DROP NOT NULL;
ALTER TABLE public.social_media_shares
    ADD COLUMN IF NOT EXISTS mockup_image_id uuid NULL REFERENCES public.mockup_images(id) ON DELETE CASCADE;
ALTER TABLE public.social_media_shares ADD COLUMN IF NOT EXISTS destination_id character varying(255) NULL;

CREATE INDEX IF NOT EXISTS idx_social_media_shares_mockup_image_id
    ON public.social_media_shares (mockup_image_id) WHERE mockup_image_id IS NOT NULL;
