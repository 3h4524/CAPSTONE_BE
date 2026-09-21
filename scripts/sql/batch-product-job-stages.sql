-- APCS batch/product/job-stage schema update.
-- Run against Neon public before running scripts/Scaffold-Database.ps1.
-- Existing products/jobs are retained and grouped into one legacy batch per owner.

BEGIN;

CREATE TABLE batches (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name character varying(255) NOT NULL,
    description text,
    default_niche character varying(150),
    default_product_type character varying(50),
    input_method character varying(20) NOT NULL,
    status character varying(20) NOT NULL DEFAULT 'draft',
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone,
    deleted_at timestamp with time zone,
    CONSTRAINT chk_batches_input_method CHECK (input_method IN ('file', 'manual')),
    CONSTRAINT chk_batches_status CHECK (status IN ('draft', 'processing', 'completed')),
    CONSTRAINT uq_batches_id_user UNIQUE (id, user_id)
);

CREATE UNIQUE INDEX uq_batches_user_name_active
    ON batches (user_id, lower(name))
    WHERE deleted_at IS NULL;
CREATE INDEX idx_batches_user_created ON batches (user_id, created_at DESC);

ALTER TABLE products ADD COLUMN batch_id uuid;
ALTER TABLE batch_jobs ADD COLUMN batch_id uuid;
ALTER TABLE batch_jobs ADD COLUMN job_type character varying(40) NOT NULL DEFAULT 'legacy';

-- Preserve any pre-existing unbatched records by putting each owner's records
-- into a single clearly named legacy batch.
INSERT INTO batches (user_id, name, input_method, status)
SELECT owners.user_id, 'Legacy data (pre-batch schema)', 'manual', 'draft'
FROM (
    SELECT user_id FROM products WHERE batch_id IS NULL
    UNION
    SELECT user_id FROM batch_jobs WHERE batch_id IS NULL
) AS owners
WHERE NOT EXISTS (
    SELECT 1
    FROM batches existing
    WHERE existing.user_id = owners.user_id
      AND existing.name = 'Legacy data (pre-batch schema)'
      AND existing.deleted_at IS NULL
);

UPDATE products product
SET batch_id = batch.id
FROM batches batch
WHERE product.batch_id IS NULL
  AND batch.user_id = product.user_id
  AND batch.name = 'Legacy data (pre-batch schema)'
  AND batch.deleted_at IS NULL;

UPDATE batch_jobs job
SET batch_id = batch.id
FROM batches batch
WHERE job.batch_id IS NULL
  AND batch.user_id = job.user_id
  AND batch.name = 'Legacy data (pre-batch schema)'
  AND batch.deleted_at IS NULL;

ALTER TABLE products ALTER COLUMN batch_id SET NOT NULL;
ALTER TABLE batch_jobs ALTER COLUMN batch_id SET NOT NULL;

ALTER TABLE batch_jobs
    ADD CONSTRAINT chk_batch_jobs_job_type
    CHECK (job_type IN ('design_generation', 'video_generation', 'listing_generation', 'legacy'));
ALTER TABLE batch_jobs DROP CONSTRAINT chk_batch_jobs_source_type;
ALTER TABLE batch_jobs
    ADD CONSTRAINT chk_batch_jobs_source_type
    CHECK (source_file_type IN ('csv', 'xls', 'xlsx', 'manual'));

ALTER TABLE products
    ADD CONSTRAINT fk_products_batch_owner
    FOREIGN KEY (batch_id, user_id) REFERENCES batches (id, user_id) ON DELETE RESTRICT;
ALTER TABLE batch_jobs
    ADD CONSTRAINT fk_batch_jobs_batch_owner
    FOREIGN KEY (batch_id, user_id) REFERENCES batches (id, user_id) ON DELETE RESTRICT;

CREATE UNIQUE INDEX uq_products_batch_name_active
    ON products (batch_id, lower(name))
    WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX uq_products_batch_id_id ON products (batch_id, id);
CREATE UNIQUE INDEX uq_batch_jobs_id_batch ON batch_jobs (id, batch_id);
CREATE INDEX idx_products_batch_status ON products (batch_id, processing_status);
CREATE INDEX idx_batch_jobs_batch_created ON batch_jobs (batch_id, created_at DESC);

ALTER TABLE batch_job_products ADD COLUMN batch_id uuid;
UPDATE batch_job_products item
SET batch_id = job.batch_id
FROM batch_jobs job
WHERE item.batch_id IS NULL
  AND item.batch_job_id = job.id;
ALTER TABLE batch_job_products ALTER COLUMN batch_id SET NOT NULL;
ALTER TABLE batch_job_products
    DROP CONSTRAINT batch_job_products_batch_job_id_fkey;
ALTER TABLE batch_job_products
    DROP CONSTRAINT batch_job_products_product_id_fkey;
ALTER TABLE batch_job_products
    ADD CONSTRAINT fk_batch_job_products_job_batch
    FOREIGN KEY (batch_job_id, batch_id) REFERENCES batch_jobs (id, batch_id) ON DELETE CASCADE;
ALTER TABLE batch_job_products
    ADD CONSTRAINT fk_batch_job_products_product_batch
    FOREIGN KEY (batch_id, product_id) REFERENCES products (batch_id, id) ON DELETE NO ACTION;
CREATE UNIQUE INDEX uq_batch_job_products_job_product
    ON batch_job_products (batch_job_id, product_id)
    WHERE product_id IS NOT NULL;
CREATE UNIQUE INDEX uq_batch_job_products_id_product
    ON batch_job_products (id, product_id);
CREATE INDEX idx_batch_job_products_batch ON batch_job_products (batch_id);

-- Link every per-product output to the exact job item that produced it. Nullable
-- values preserve any historical outputs that cannot be attributed unambiguously.
ALTER TABLE ai_prompts ADD COLUMN batch_job_product_id uuid;
ALTER TABLE design_images ADD COLUMN batch_job_product_id uuid;
ALTER TABLE mockup_images ADD COLUMN batch_job_product_id uuid;
ALTER TABLE promo_videos ADD COLUMN batch_job_product_id uuid;
ALTER TABLE listing_contents ADD COLUMN batch_job_product_id uuid;

ALTER TABLE ai_prompts
    ADD CONSTRAINT fk_ai_prompts_job_product
    FOREIGN KEY (batch_job_product_id) REFERENCES batch_job_products (id) ON DELETE SET NULL;
ALTER TABLE design_images
    ADD CONSTRAINT fk_design_images_job_product
    FOREIGN KEY (batch_job_product_id) REFERENCES batch_job_products (id) ON DELETE SET NULL;
ALTER TABLE mockup_images
    ADD CONSTRAINT fk_mockup_images_job_product
    FOREIGN KEY (batch_job_product_id) REFERENCES batch_job_products (id) ON DELETE SET NULL;
ALTER TABLE promo_videos
    ADD CONSTRAINT fk_promo_videos_job_product
    FOREIGN KEY (batch_job_product_id) REFERENCES batch_job_products (id) ON DELETE SET NULL;
ALTER TABLE listing_contents
    ADD CONSTRAINT fk_listing_contents_job_product
    FOREIGN KEY (batch_job_product_id) REFERENCES batch_job_products (id) ON DELETE SET NULL;

CREATE INDEX idx_ai_prompts_job_product ON ai_prompts (batch_job_product_id);
CREATE INDEX idx_design_images_job_product ON design_images (batch_job_product_id);
CREATE INDEX idx_mockup_images_job_product ON mockup_images (batch_job_product_id);
CREATE INDEX idx_promo_videos_job_product ON promo_videos (batch_job_product_id);
CREATE INDEX idx_listing_contents_job_product ON listing_contents (batch_job_product_id);

COMMIT;
