-- An AI render job now waits in 'generating_clips' while the API makes its AI scenes, and only becomes 'queued'
-- (the state the media worker and the Streams outbox look for) once every clip is stored, so the worker spends
-- its time rendering and not waiting for the video provider.
-- Backward compatible: the old release never writes the new status, and the outbox triggers only fire on 'queued'.
ALTER TABLE public.media_jobs DROP CONSTRAINT IF EXISTS media_jobs_status_check;
ALTER TABLE public.media_jobs ADD CONSTRAINT media_jobs_status_check CHECK (
    status::text = ANY (ARRAY['queued', 'generating_clips', 'leased', 'succeeded', 'failed', 'cancelled']::text[]));

-- A node still has at most one job that is waiting, generating its clips or being worked on.
DROP INDEX IF EXISTS public.uq_media_job_node_active;
CREATE UNIQUE INDEX uq_media_job_node_active ON public.media_jobs USING btree (workflow_node_run_id, kind)
    WHERE status::text = ANY ((ARRAY['queued'::character varying, 'generating_clips'::character varying, 'leased'::character varying])::text[]);

-- The clip phase reads the jobs it has to look after, oldest first.
CREATE INDEX IF NOT EXISTS idx_media_jobs_generating_clips ON public.media_jobs USING btree (available_at)
    WHERE status::text = 'generating_clips';
