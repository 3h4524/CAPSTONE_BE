-- One video per (product, design) of a batch run. A run group holds the workflow snapshot the seller started,
-- waits at Mockup Approval until the seller confirms which designs get a video, then owns one workflow run per
-- chosen design. Each child run is an ordinary video run, so retry, review and export work on it as before.

CREATE TABLE IF NOT EXISTS public.workflow_run_groups (
    id uuid NOT NULL,
    workflow_id uuid NOT NULL,
    user_id uuid NOT NULL,
    batch_id uuid NOT NULL,
    batch_job_id uuid,
    workflow_revision bigint NOT NULL,
    definition_snapshot jsonb NOT NULL,
    -- Per design scene image choices made before rendering, keyed by "<productId>:<artworkGroupKey>".
    target_overrides jsonb DEFAULT '{}'::jsonb NOT NULL,
    status character varying(40) NOT NULL,
    revision bigint DEFAULT 1 NOT NULL,
    idempotency_key character varying(100) NOT NULL,
    request_hash character varying(64) NOT NULL,
    error_message character varying(1000),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    completed_at timestamp with time zone,
    CONSTRAINT workflow_run_groups_pkey PRIMARY KEY (id),
    CONSTRAINT workflow_run_groups_status_check CHECK (((status)::text = ANY (ARRAY[('waiting_for_targets'::character varying)::text, ('running'::character varying)::text, ('completed'::character varying)::text, ('cancelled'::character varying)::text]))),
    CONSTRAINT workflow_run_groups_user_id_idempotency_key_key UNIQUE (user_id, idempotency_key),
    CONSTRAINT workflow_run_groups_workflow_id_fkey FOREIGN KEY (workflow_id) REFERENCES public.workflows(id),
    CONSTRAINT workflow_run_groups_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id)
);

CREATE INDEX IF NOT EXISTS idx_workflow_run_groups_owner_workflow
    ON public.workflow_run_groups USING btree (user_id, workflow_id, created_at DESC);

ALTER TABLE public.workflow_runs ADD COLUMN IF NOT EXISTS run_group_id uuid;
ALTER TABLE public.workflow_runs ADD COLUMN IF NOT EXISTS artwork_group_key character varying(100);

ALTER TABLE ONLY public.workflow_runs
    ADD CONSTRAINT workflow_runs_run_group_id_fkey FOREIGN KEY (run_group_id) REFERENCES public.workflow_run_groups(id);

CREATE INDEX IF NOT EXISTS idx_workflow_runs_run_group
    ON public.workflow_runs USING btree (run_group_id) WHERE (run_group_id IS NOT NULL);

-- The background planner picks the oldest queued child run.
CREATE INDEX IF NOT EXISTS idx_workflow_runs_group_queue
    ON public.workflow_runs USING btree (created_at) WHERE (((status)::text = 'queued'::text) AND (run_group_id IS NOT NULL));
