-- With Video:DeferredRender enabled, Generate Video finishes as soon as the video's scenes are ready to be shown, and the
-- final video is rendered later, in the background, only when a step needs the file (Publish to Etsy, or Export ZIP with
-- its "final video" option on).
--
--  * promo_videos.status 'ready': every scene (photos, and AI clips once stored) is available, but no video file exists yet.
--    The seller previews and edits it in the browser. ('rendering' was already allowed and is now used while the file is made.)
--  * media_jobs.status 'awaiting_render': the render job of a ready video. The media worker and the Streams outbox only look
--    at 'queued' jobs, so nobody picks it up until the API queues it.
--
-- Backward compatible: the old release never writes the new statuses, and the outbox triggers only fire on 'queued'.
ALTER TABLE public.promo_videos DROP CONSTRAINT IF EXISTS chk_promo_videos_status;
ALTER TABLE public.promo_videos ADD CONSTRAINT chk_promo_videos_status CHECK (
    status::text = ANY (ARRAY['pending', 'queued', 'ready', 'rendering', 'completed', 'failed']::text[]));

ALTER TABLE public.media_jobs DROP CONSTRAINT IF EXISTS media_jobs_status_check;
ALTER TABLE public.media_jobs ADD CONSTRAINT media_jobs_status_check CHECK (
    status::text = ANY (ARRAY['queued', 'generating_clips', 'awaiting_render', 'leased', 'succeeded', 'failed', 'cancelled']::text[]));

-- A node still has at most one job that is waiting, generating its clips, waiting for a render to be asked for, or being worked on.
DROP INDEX IF EXISTS public.uq_media_job_node_active;
CREATE UNIQUE INDEX uq_media_job_node_active ON public.media_jobs USING btree (workflow_node_run_id, kind)
    WHERE status::text = ANY ((ARRAY['queued'::character varying, 'generating_clips'::character varying, 'awaiting_render'::character varying, 'leased'::character varying])::text[]);
