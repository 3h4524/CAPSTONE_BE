-- Makes the basic pipeline steps plan features an Admin can put on and take off a plan, and caps
-- listings per month.
--
-- New features:
--   design_images     Design images from a prompt.
--   template_mockups  Mockups rendered from the built-in templates.
--   standard_video    Standard videos, square and portrait (landscape stays all_video_formats).
--   zip_export        A product's files as a ZIP.
--   monthly_listings  Listings a paid plan may write per calendar month (UTC); no row means no limit.
--
-- Every plan that exists gets the four basic steps switched on, so nobody loses them. The first run
-- also switches Listing content on for Starter, capped at 10 listings a month. Later runs leave the
-- plans as an Admin has set them.
--
-- Additive and safe to run more than once. Needs plan-feature-catalog.sql first.
-- Review it against the target database before applying.

BEGIN;

INSERT INTO features (code, label, description, group_name, kind, unit, default_limit, trial_available, sort_order)
VALUES
    ('design_images', 'Design images from prompt',
     'Generate design images from a prompt, on their own or in a workflow.',
     'Design images', 'flag', NULL, NULL, TRUE, 5),
    ('template_mockups', 'Mockups from templates',
     'Render mockups with the built-in templates.',
     'Mockups', 'flag', NULL, NULL, TRUE, 25),
    ('standard_video', 'Standard video',
     'Render standard videos in square and portrait.',
     'Video', 'flag', NULL, NULL, TRUE, 55),
    ('zip_export', 'ZIP export',
     'Download a finished product''s files as a ZIP.',
     'Workflow', 'flag', NULL, NULL, TRUE, 125),
    ('monthly_listings', 'Listings per month',
     'Most listings a Seller can write each calendar month. Leave empty for no limit.',
     'Listing', 'limit', 'per month', 10, TRUE, 45)
ON CONFLICT (code) DO NOTHING;

-- The basic steps stay on for every plan that exists.
INSERT INTO plan_features (plan_id, feature_code, is_enabled, limit_value, created_at)
SELECT plan.id, basic.code, TRUE, NULL, now()
FROM subscription_plans AS plan
CROSS JOIN (VALUES ('design_images'), ('template_mockups'), ('standard_video'), ('zip_export')) AS basic (code)
ON CONFLICT (plan_id, feature_code) DO NOTHING;

-- Once only: Starter writes listings, up to 10 a month.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM subscription_plans WHERE tier = 'starter')
       AND NOT EXISTS (
           SELECT 1 FROM plan_features AS feature
           JOIN subscription_plans AS plan ON plan.id = feature.plan_id
           WHERE plan.tier = 'starter' AND feature.feature_code = 'monthly_listings')
    THEN
        INSERT INTO plan_features (plan_id, feature_code, is_enabled, limit_value, created_at)
        SELECT id, 'monthly_listings', TRUE, 10, now() FROM subscription_plans WHERE tier = 'starter';

        INSERT INTO plan_features (plan_id, feature_code, is_enabled, limit_value, created_at)
        SELECT id, 'workflow_listing', TRUE, NULL, now() FROM subscription_plans WHERE tier = 'starter'
        ON CONFLICT (plan_id, feature_code) DO UPDATE SET is_enabled = TRUE;
    END IF;
END $$;

COMMIT;
