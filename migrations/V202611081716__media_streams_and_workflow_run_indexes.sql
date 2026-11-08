-- The trigger makes the job transition and its outbox message part of the same transaction.
ALTER TABLE public.media_jobs ADD COLUMN IF NOT EXISTS queue_version bigint NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS public.media_job_outbox (
    event_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id uuid NOT NULL REFERENCES public.media_jobs(id) ON DELETE CASCADE,
    queue_version bigint NOT NULL,
    kind text NOT NULL CHECK (kind IN ('render', 'export_zip')),
    available_at timestamp without time zone NOT NULL,
    published_at timestamp without time zone NULL,
    claim_token uuid NULL,
    claim_until timestamp without time zone NULL,
    attempts integer NOT NULL DEFAULT 0,
    next_attempt_at timestamp without time zone NOT NULL,
    created_at timestamp without time zone NOT NULL DEFAULT (now() AT TIME ZONE 'UTC'),
    UNIQUE (job_id, queue_version)
);
CREATE INDEX IF NOT EXISTS idx_media_outbox_dispatch ON public.media_job_outbox (next_attempt_at, available_at)
    WHERE published_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_media_outbox_published ON public.media_job_outbox (published_at)
    WHERE published_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_media_jobs_stream_recovery ON public.media_jobs (status, lease_expires_at, available_at)
    WHERE kind IN ('render', 'export_zip');

CREATE OR REPLACE FUNCTION public.media_job_queue_version() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.kind IN ('render', 'export_zip') AND NEW.status = 'queued' AND
       (TG_OP = 'INSERT' OR OLD.status IS DISTINCT FROM NEW.status OR OLD.payload IS DISTINCT FROM NEW.payload
        OR OLD.available_at IS DISTINCT FROM NEW.available_at OR OLD.attempt IS DISTINCT FROM NEW.attempt) THEN
        NEW.queue_version := COALESCE(NEW.queue_version, 0) + 1;
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS media_job_queue_version_trigger ON public.media_jobs;
CREATE TRIGGER media_job_queue_version_trigger BEFORE INSERT OR UPDATE ON public.media_jobs
    FOR EACH ROW EXECUTE FUNCTION public.media_job_queue_version();

CREATE OR REPLACE FUNCTION public.media_job_create_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.kind IN ('render', 'export_zip') AND NEW.status = 'queued' AND
       (TG_OP = 'INSERT' OR OLD.queue_version IS DISTINCT FROM NEW.queue_version) THEN
        INSERT INTO public.media_job_outbox(job_id, queue_version, kind, available_at, next_attempt_at)
        VALUES (NEW.id, NEW.queue_version, NEW.kind, NEW.available_at, NEW.available_at)
        ON CONFLICT (job_id, queue_version) DO NOTHING;
        PERFORM pg_notify('apcs_media_outbox', 'ready');
    END IF;
    RETURN NULL;
END $$;
DROP TRIGGER IF EXISTS media_job_create_outbox_trigger ON public.media_jobs;
CREATE TRIGGER media_job_create_outbox_trigger AFTER INSERT OR UPDATE ON public.media_jobs
    FOR EACH ROW EXECUTE FUNCTION public.media_job_create_outbox();

-- Backfill jobs that were already waiting when the schema was installed.
UPDATE public.media_jobs SET queue_version = queue_version + 1
WHERE kind IN ('render', 'export_zip') AND status = 'queued' AND queue_version = 0;
INSERT INTO public.media_job_outbox(job_id, queue_version, kind, available_at, next_attempt_at)
SELECT id, queue_version, kind, available_at, available_at FROM public.media_jobs
WHERE kind IN ('render', 'export_zip') AND status = 'queued'
ON CONFLICT (job_id, queue_version) DO NOTHING;

CREATE INDEX IF NOT EXISTS idx_workflow_runs_request_hash ON public.workflow_runs USING btree (user_id, request_hash);
CREATE INDEX IF NOT EXISTS idx_workflow_runs_video_batch ON public.workflow_runs USING btree (user_id, (((definition_snapshot -> 'videoBatch'::text) ->> 'id'::text))) WHERE (definition_snapshot ? 'videoBatch'::text);
CREATE INDEX IF NOT EXISTS idx_workflow_runs_video_batch_history ON public.workflow_runs USING btree (user_id, workflow_id, created_at DESC) WHERE (definition_snapshot ? 'videoBatch'::text);
