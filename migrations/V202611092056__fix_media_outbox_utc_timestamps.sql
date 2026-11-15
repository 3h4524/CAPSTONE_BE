-- The outbox keeps UTC wall-clock times (timestamp without time zone) and the dispatcher compares them with
-- now() AT TIME ZONE 'UTC'. The trigger copied media_jobs.available_at (timestamptz) with an implicit cast,
-- which uses the session time zone: on a server whose time zone is not UTC (Asia/Bangkok, +07) a new event
-- became due hours late, so renders stayed queued and no worker ever received them.
CREATE OR REPLACE FUNCTION public.media_job_create_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.kind IN ('render', 'export_zip') AND NEW.status = 'queued' AND
       (TG_OP = 'INSERT' OR OLD.queue_version IS DISTINCT FROM NEW.queue_version) THEN
        INSERT INTO public.media_job_outbox(job_id, queue_version, kind, available_at, next_attempt_at)
        VALUES (NEW.id, NEW.queue_version, NEW.kind,
                NEW.available_at AT TIME ZONE 'UTC', NEW.available_at AT TIME ZONE 'UTC')
        ON CONFLICT (job_id, queue_version) DO NOTHING;
        PERFORM pg_notify('apcs_media_outbox', 'ready');
    END IF;
    RETURN NULL;
END $$;

-- Repair events the trigger wrote that were never claimed. Matching the job's current queue version guarantees
-- its available_at is the value the event was written from; on a UTC server this rewrites the same values.
UPDATE public.media_job_outbox o
SET available_at = j.available_at AT TIME ZONE 'UTC',
    next_attempt_at = j.available_at AT TIME ZONE 'UTC'
FROM public.media_jobs j
WHERE o.job_id = j.id AND o.queue_version = j.queue_version
  AND o.published_at IS NULL AND o.claim_token IS NULL AND o.attempts = 0;
