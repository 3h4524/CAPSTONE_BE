-- The test reports the admin pages read.
--
-- One row per run of the end-to-end suite. The report Playwright wrote is kept as it is, so the
-- pages can read the tests of a run without the file the suite wrote being around any more.
--
-- The table is created in the database first; this file is the record of what was applied.

CREATE TABLE IF NOT EXISTS public.test_report_runs (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    run_id character varying(64) NOT NULL,
    started_at_utc timestamp with time zone NOT NULL,
    finished_at_utc timestamp with time zone,
    duration_ms bigint DEFAULT 0 NOT NULL,
    status character varying(20) NOT NULL,
    branch character varying(255),
    commit_sha character varying(64),
    triggered_by character varying(320),
    total_tests integer DEFAULT 0 NOT NULL,
    passed_tests integer DEFAULT 0 NOT NULL,
    failed_tests integer DEFAULT 0 NOT NULL,
    flaky_tests integer DEFAULT 0 NOT NULL,
    skipped_tests integer DEFAULT 0 NOT NULL,
    known_issue_tests integer DEFAULT 0 NOT NULL,
    unexpected_failed_tests integer DEFAULT 0 NOT NULL,
    file_count integer DEFAULT 0 NOT NULL,
    suite_count integer DEFAULT 0 NOT NULL,
    failure_summary text,
    report jsonb,
    created_at_utc timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT pk_test_report_runs PRIMARY KEY (id),
    CONSTRAINT uq_test_report_runs_run_id UNIQUE (run_id),
    CONSTRAINT chk_test_report_runs_status CHECK (((status)::text = ANY (ARRAY[('running'::character varying)::text, ('passed'::character varying)::text, ('failed'::character varying)::text, ('error'::character varying)::text])))
);
