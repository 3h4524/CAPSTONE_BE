-- Apply once: offline image-quality scoring runs an admin uploads (offline scoring scripts, kept outside the repo), one row per uploaded export file.
-- report holds the run's prompt versions and per-image scores as JSON; thumbnails live in Cloudinary, not here.
BEGIN;
CREATE TABLE IF NOT EXISTS public.image_eval_runs (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name varchar(200) NOT NULL,
    notes text NULL,
    image_count integer DEFAULT 0 NOT NULL,
    product_count integer DEFAULT 0 NOT NULL,
    version_count integer DEFAULT 0 NOT NULL,
    report jsonb NOT NULL,
    uploaded_by varchar(320) NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_image_eval_runs PRIMARY KEY (id),
    CONSTRAINT chk_image_eval_runs_counts CHECK (image_count >= 0 AND product_count >= 0 AND version_count >= 0),
    CONSTRAINT chk_image_eval_runs_report CHECK (jsonb_typeof(report) = 'object')
);
CREATE INDEX IF NOT EXISTS ix_image_eval_runs_created_at ON public.image_eval_runs (created_at DESC);
COMMIT;
