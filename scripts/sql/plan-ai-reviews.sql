-- Stores the AI review of the paid plans shown in the Admin Portal's Plan Insights.
--
-- A background job writes a row when the latest successful review is older than the refresh
-- interval (14 days by default), and an Admin can ask for one sooner. Failed attempts are kept
-- too, so the job can wait before retrying and an Admin can see why a review is missing.
-- input_json holds the figures sent to the model, so every review can be checked against them.
-- raw_answer keeps the model's own answer, so the prompt can be fixed where it goes wrong, and
-- dropped_parts lists what the backend left out of or rewrote in a stored review, and why.
--
-- Additive and safe to run more than once. Review it against the target database before applying.

BEGIN;

CREATE TABLE IF NOT EXISTS plan_ai_reviews (
    id            uuid        NOT NULL DEFAULT gen_random_uuid(),
    status        varchar(20) NOT NULL,
    period_start  date        NOT NULL,
    period_end    date        NOT NULL,
    model         varchar(100),
    input_json    jsonb       NOT NULL,
    review_json   jsonb,
    input_tokens  integer,
    output_tokens integer,
    error_message text,
    raw_answer    text,
    dropped_parts text,
    triggered_by  uuid,
    generated_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_plan_ai_reviews PRIMARY KEY (id),
    CONSTRAINT ck_plan_ai_reviews_status CHECK (status IN ('succeeded', 'failed')),
    CONSTRAINT ck_plan_ai_reviews_review CHECK (status <> 'succeeded' OR review_json IS NOT NULL),
    CONSTRAINT fk_plan_ai_reviews_triggered_by FOREIGN KEY (triggered_by) REFERENCES users (id) ON DELETE SET NULL
);

-- Added after the table was first created.
ALTER TABLE plan_ai_reviews ADD COLUMN IF NOT EXISTS raw_answer text;
ALTER TABLE plan_ai_reviews ADD COLUMN IF NOT EXISTS dropped_parts text;

CREATE INDEX IF NOT EXISTS idx_plan_ai_reviews_status_generated_at
    ON plan_ai_reviews (status, generated_at DESC);

COMMIT;
