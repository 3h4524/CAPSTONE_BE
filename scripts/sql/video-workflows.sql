BEGIN;

-- Preserve existing repository features when scaffolding an older database.
ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_subject text;
ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_art_style text;
ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_mood_tone text;
ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_negative_terms text;
ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_instructions text;
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS user_id uuid REFERENCES public.users(id);

CREATE TABLE IF NOT EXISTS public.workflows (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES public.users(id),
    name varchar(120) NOT NULL,
    description varchar(500) NOT NULL DEFAULT '',
    definition jsonb NOT NULL,
    schema_version integer NOT NULL DEFAULT 2 CHECK (schema_version = 2),
    revision bigint NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    deleted_at timestamptz
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_workflow_owner_name ON public.workflows(user_id, lower(name)) WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS public.workflow_runs (
    id uuid PRIMARY KEY,
    workflow_id uuid NOT NULL REFERENCES public.workflows(id),
    user_id uuid NOT NULL REFERENCES public.users(id),
    product_id uuid NOT NULL REFERENCES public.products(id),
    workflow_revision bigint NOT NULL,
    definition_snapshot jsonb NOT NULL,
    status varchar(40) NOT NULL CHECK (status IN ('queued','running','waiting_for_input','waiting_for_review','completed','failed','cancelled')),
    revision bigint NOT NULL DEFAULT 1,
    idempotency_key varchar(100) NOT NULL,
    request_hash varchar(64) NOT NULL,
    parent_run_id uuid REFERENCES public.workflow_runs(id),
    error_message varchar(1000),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz,
    UNIQUE(user_id, idempotency_key)
);
CREATE INDEX IF NOT EXISTS idx_workflow_runs_owner_workflow ON public.workflow_runs(user_id, workflow_id, created_at DESC);

CREATE TABLE IF NOT EXISTS public.workflow_node_runs (
    id uuid PRIMARY KEY,
    workflow_run_id uuid NOT NULL REFERENCES public.workflow_runs(id),
    node_id varchar(100) NOT NULL,
    node_type varchar(50) NOT NULL,
    status varchar(40) NOT NULL CHECK (status IN ('pending','running','waiting_for_input','waiting_for_review','succeeded','failed','skipped','cancelled')),
    attempt integer NOT NULL DEFAULT 1,
    input_snapshot jsonb NOT NULL DEFAULT '{}',
    output_snapshot jsonb NOT NULL DEFAULT '{}',
    stage varchar(50),
    progress integer NOT NULL DEFAULT 0 CHECK (progress BETWEEN 0 AND 100),
    error_message varchar(1000),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(workflow_run_id, node_id)
);

CREATE TABLE IF NOT EXISTS public.media_jobs (
    id uuid PRIMARY KEY,
    workflow_run_id uuid NOT NULL REFERENCES public.workflow_runs(id),
    workflow_node_run_id uuid NOT NULL REFERENCES public.workflow_node_runs(id),
    user_id uuid NOT NULL REFERENCES public.users(id),
    kind varchar(20) NOT NULL CHECK (kind IN ('render','export_zip')),
    status varchar(20) NOT NULL CHECK (status IN ('queued','leased','succeeded','failed','cancelled')),
    payload jsonb NOT NULL,
    attempt integer NOT NULL DEFAULT 0,
    maximum_attempts integer NOT NULL DEFAULT 3,
    lease_token uuid,
    lease_expires_at timestamptz,
    heartbeat_at timestamptz,
    available_at timestamptz NOT NULL DEFAULT now(),
    progress integer NOT NULL DEFAULT 0 CHECK (progress BETWEEN 0 AND 100),
    stage varchar(50),
    error_message varchar(1000),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_media_jobs_claim ON public.media_jobs(status, available_at, lease_expires_at);
CREATE UNIQUE INDEX IF NOT EXISTS uq_media_job_node_active ON public.media_jobs(workflow_node_run_id) WHERE status IN ('queued','leased');

ALTER TABLE public.mockup_images ALTER COLUMN design_image_id DROP NOT NULL;
ALTER TABLE public.mockup_images ALTER COLUMN mockup_template_id DROP NOT NULL;
ALTER TABLE public.mockup_images ALTER COLUMN mockup_image_url DROP NOT NULL;
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS source_type varchar(20) NOT NULL DEFAULT 'generated';
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS content_hash varchar(64);
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS metadata_revision bigint NOT NULL DEFAULT 1;
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS artwork_group_key varchar(100);
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS variant_key varchar(100);
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS role varchar(30) NOT NULL DEFAULT 'Hero';
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS regions jsonb NOT NULL DEFAULT '{}';
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS storage_version varchar(40);
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS approved_revision bigint;
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS approved_by uuid REFERENCES public.users(id);
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS approved_at timestamptz;
UPDATE public.mockup_images SET artwork_group_key = COALESCE(design_image_id::text, product_id::text) WHERE artwork_group_key IS NULL;
ALTER TABLE public.mockup_images DROP CONSTRAINT IF EXISTS chk_mockup_source;
ALTER TABLE public.mockup_images ADD CONSTRAINT chk_mockup_source CHECK (source_type = 'uploaded' OR (source_type = 'generated' AND design_image_id IS NOT NULL AND mockup_template_id IS NOT NULL));

ALTER TABLE public.video_templates DROP CONSTRAINT IF EXISTS chk_video_templates_duration;
ALTER TABLE public.video_templates ADD CONSTRAINT chk_video_templates_duration CHECK (duration_seconds BETWEEN 3 AND 30);
ALTER TABLE public.video_templates ADD COLUMN IF NOT EXISTS code varchar(50);
ALTER TABLE public.video_templates ADD COLUMN IF NOT EXISTS template_version integer NOT NULL DEFAULT 1;
CREATE UNIQUE INDEX IF NOT EXISTS uq_video_template_code_version ON public.video_templates(code, template_version) WHERE code IS NOT NULL;
INSERT INTO public.video_templates(id,name,type,platform,duration_seconds,aspect_ratio,resolution,effects_config,preview_video_url,is_system_template,is_active,code,template_version)
VALUES
 ('a1dc0c01-7336-4e24-a173-7a882e310001','Product Showcase','product_showcase','etsy',12,'1:2','1080x2160','{}',null,true,true,'product_showcase',1),
 ('a1dc0c01-7336-4e24-a173-7a882e310002','Design Detail','product_showcase','etsy',12,'1:2','1080x2160','{}',null,true,true,'design_detail',1),
 ('a1dc0c01-7336-4e24-a173-7a882e310003','Variant Showcase','product_showcase','etsy',12,'1:2','1080x2160','{}',null,true,true,'variant_showcase',1),
 ('a1dc0c02-7336-4e24-a173-7a882e310001','Product Showcase','product_showcase','etsy',12,'1:2','1080x2160','{"description":"A balanced product-first video that works with one or more approved mockups.","defaultTransition":"fade","defaultMotionPreset":"varied","requirements":{"minimumAssets":1,"requiresDetail":false,"minimumVariants":0}}','/video-templates/product-showcase-v2.mp4',true,true,'product_showcase',2),
 ('a1dc0c02-7336-4e24-a173-7a882e310002','Design Detail','product_showcase','etsy',12,'1:2','1080x2160','{"description":"Moves from the complete product to a close look at the artwork.","defaultTransition":"fade","defaultMotionPreset":"varied","requirements":{"minimumAssets":1,"requiresDetail":true,"minimumVariants":0}}','/video-templates/design-detail-v2.mp4',true,true,'design_detail',2),
 ('a1dc0c02-7336-4e24-a173-7a882e310003','Variant Showcase','product_showcase','etsy',12,'1:2','1080x2160','{"description":"Shows distinct product variants before returning to the strongest view.","defaultTransition":"fade","defaultMotionPreset":"varied","requirements":{"minimumAssets":2,"requiresDetail":false,"minimumVariants":2}}','/video-templates/variant-showcase-v2.mp4',true,true,'variant_showcase',2)
ON CONFLICT(id) DO NOTHING;

ALTER TABLE public.promo_videos DROP CONSTRAINT IF EXISTS chk_promo_videos_duration;
ALTER TABLE public.promo_videos ADD CONSTRAINT chk_promo_videos_duration CHECK (video_duration_seconds BETWEEN 3 AND 30);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS mode varchar(30) NOT NULL DEFAULT 'standard' CHECK (mode = 'standard');
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS series_id uuid;
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS version_number integer NOT NULL DEFAULT 1;
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS workflow_run_id uuid REFERENCES public.workflow_runs(id);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS fingerprint varchar(64);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS template_version integer NOT NULL DEFAULT 1;
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS config_snapshot jsonb NOT NULL DEFAULT '{}';
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS qa_result jsonb NOT NULL DEFAULT '{}';
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS review_revision bigint NOT NULL DEFAULT 1;
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS approved_by uuid REFERENCES public.users(id);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS approved_at timestamptz;
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS thumbnail_storage_key varchar(500);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS storage_version varchar(40);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS thumbnail_storage_version varchar(40);
CREATE UNIQUE INDEX IF NOT EXISTS uq_promo_video_series_version ON public.promo_videos(series_id,version_number) WHERE series_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_promo_video_fingerprint ON public.promo_videos(product_id,fingerprint);

ALTER TABLE public.promo_video_scenes ADD COLUMN IF NOT EXISTS generation_strategy varchar(30) NOT NULL DEFAULT 'standard';
ALTER TABLE public.promo_video_scenes ADD COLUMN IF NOT EXISTS source_revision bigint;
ALTER TABLE public.promo_video_scenes ADD COLUMN IF NOT EXISTS source_hash varchar(64);
ALTER TABLE public.promo_video_scenes ADD COLUMN IF NOT EXISTS scene_config jsonb NOT NULL DEFAULT '{}';
ALTER TABLE public.promo_video_scenes ADD COLUMN IF NOT EXISTS warnings jsonb NOT NULL DEFAULT '[]';
ALTER TABLE public.export_package_items ADD COLUMN IF NOT EXISTS promo_video_id uuid REFERENCES public.promo_videos(id);
ALTER TABLE public.export_packages ADD COLUMN IF NOT EXISTS workflow_run_id uuid REFERENCES public.workflow_runs(id);
ALTER TABLE public.export_packages ADD COLUMN IF NOT EXISTS manifest jsonb NOT NULL DEFAULT '{}';

COMMIT;
