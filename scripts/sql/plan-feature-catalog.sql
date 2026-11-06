-- The catalog of plan features an Admin can put on a plan.
--
-- Each row is a feature the backend already gates in code; the code is the key the backend checks,
-- so a new feature still needs a developer. What an Admin edits here is how a feature is shown:
-- its label, description, group and order, whether it is offered when adding features to a plan,
-- and whether the free trial includes it.
--
-- The seed matches PlanFeatures in the backend and never overwrites an Admin's edits.
-- plan_features.feature_code references this table only when every code already stored is in the
-- catalog: a database still holding older codes keeps working, and the check can be run again
-- after it is cleaned up.
--
-- Additive and safe to run more than once. Review it against the target database before applying.

BEGIN;

CREATE TABLE IF NOT EXISTS features (
    code            varchar(100) NOT NULL,
    label           varchar(120) NOT NULL,
    description     text         NOT NULL DEFAULT '',
    group_name      varchar(60)  NOT NULL,
    kind            varchar(10)  NOT NULL,
    unit            varchar(40),
    default_limit   integer,
    trial_available boolean      NOT NULL DEFAULT TRUE,
    is_active       boolean      NOT NULL DEFAULT TRUE,
    sort_order      integer      NOT NULL DEFAULT 0,
    created_at      timestamptz  NOT NULL DEFAULT now(),
    updated_at      timestamptz,
    updated_by      uuid,
    CONSTRAINT pk_features PRIMARY KEY (code),
    CONSTRAINT ck_features_kind CHECK (kind IN ('flag', 'limit')),
    CONSTRAINT ck_features_default_limit CHECK (default_limit IS NULL OR default_limit >= 0),
    CONSTRAINT fk_features_updated_by FOREIGN KEY (updated_by) REFERENCES users (id) ON DELETE SET NULL
);

INSERT INTO features (code, label, description, group_name, kind, unit, default_limit, trial_available, sort_order)
VALUES
    ('design_variations', 'Design variations per product',
     'How many design variations a Seller can generate for one product.',
     'Design images', 'limit', 'variations', 2, TRUE, 10),
    ('auto_approval', 'Automatic design approval',
     'Approve generated designs automatically instead of reviewing each one.',
     'Design images', 'flag', NULL, NULL, TRUE, 20),
    ('custom_mockup_templates', 'Custom mockup templates',
     'Create and edit the Seller''s own mockup templates and garment masks.',
     'Mockups', 'flag', NULL, NULL, TRUE, 30),
    ('lifestyle_mockup', 'Lifestyle mockups (AI)',
     'Generate lifestyle mockups with the Seller''s own Gemini key.',
     'Mockups', 'flag', NULL, NULL, TRUE, 40),
    ('listing_regenerate', 'Listing regeneration and SEO score',
     'Regenerate a listing and score its SEO.',
     'Listing', 'flag', NULL, NULL, TRUE, 50),
    ('all_video_formats', 'Landscape video',
     'Render standard videos in landscape as well as square and portrait.',
     'Video', 'flag', NULL, NULL, TRUE, 60),
    ('ai_video', 'AI video (Veo)',
     'Build videos from AI scenes, billed to the Seller''s own Google key.',
     'Video', 'flag', NULL, NULL, FALSE, 70),
    ('studio_keyframe', 'AI studio keyframes',
     'Generate AI studio start frames for AI video scenes.',
     'Video', 'flag', NULL, NULL, FALSE, 80),
    ('daily_mockup_cap', 'Daily mockup safety cap',
     'Most mockups rendered per day. Leave empty for no cap.',
     'Safety caps', 'limit', 'per day', 300, TRUE, 90),
    ('daily_listing_cap', 'Daily listing safety cap',
     'Most listings generated per day. Leave empty for no cap.',
     'Safety caps', 'limit', 'per day', 100, TRUE, 100),
    ('daily_video_cap', 'Daily video safety cap',
     'Most standard videos rendered per day. Leave empty for no cap.',
     'Safety caps', 'limit', 'per day', 50, TRUE, 110),
    ('workflow_listing', 'Listing content',
     'Write listing titles, tags and descriptions, in a workflow or on their own.',
     'Workflow', 'flag', NULL, NULL, TRUE, 120),
    ('workflow_publish', 'Publish to Etsy and Printify',
     'Publish finished products to Etsy and Printify, once those steps are available.',
     'Workflow', 'flag', NULL, NULL, TRUE, 130)
ON CONFLICT (code) DO NOTHING;

-- Link plan_features to the catalog once every stored code is in it.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_plan_features_feature_code')
       AND NOT EXISTS (
           SELECT 1 FROM plan_features AS plan_feature
           WHERE NOT EXISTS (SELECT 1 FROM features AS feature WHERE feature.code = plan_feature.feature_code))
    THEN
        ALTER TABLE plan_features
            ADD CONSTRAINT fk_plan_features_feature_code
            FOREIGN KEY (feature_code) REFERENCES features (code) ON UPDATE CASCADE;
    END IF;
END $$;

COMMIT;
