-- Moves subscription plans from monthly quotas to feature tiers.
--
-- Starter and Pro (the former Creator plan) are sold. Studio stops being sold and its
-- subscribers move to Pro at their next renewal. Vip becomes an internal plan with every
-- feature. What each plan includes lives in plan_features, which the Admin Portal edits.
--
-- Safe to run more than once. Review it against the target database before applying.

BEGIN;

UPDATE subscription_plans
SET name = 'Pro',
    tier = 'pro',
    description = 'Everything in Starter plus the AI tools: lifestyle mockups, AI video, automatic approval, custom mockup templates and listing regeneration.',
    max_batch_size = 200,
    max_concurrent_jobs = 5,
    priority_support = TRUE,
    is_active = TRUE,
    updated_at = now()
WHERE tier = 'creator';

UPDATE subscription_plans
SET description = 'Run the whole pipeline: design images, mockups, listings, standard videos and ZIP export.',
    max_batch_size = 20,
    max_concurrent_jobs = 1,
    priority_support = FALSE,
    is_active = TRUE,
    updated_at = now()
WHERE tier = 'starter';

UPDATE subscription_plans
SET is_active = FALSE,
    updated_at = now()
WHERE tier = 'studio';

UPDATE subscription_plans
SET is_active = FALSE,
    max_batch_size = -1,
    max_concurrent_jobs = 10,
    priority_support = TRUE,
    updated_at = now()
WHERE tier = 'vip';

-- Studio subscribers keep Studio until their renewal date, then continue on Pro.
UPDATE subscriptions AS s
SET scheduled_plan_id = pro.id,
    scheduled_plan_effective_date = s.renewal_date,
    updated_at = now()
FROM subscription_plans AS studio, subscription_plans AS pro
WHERE studio.id = s.plan_id
  AND studio.tier = 'studio'
  AND pro.tier = 'pro'
  AND s.status = 'active'
  AND s.deleted_at IS NULL
  AND s.scheduled_plan_id IS NULL;

-- A flag uses is_enabled; a limit uses limit_value, where NULL means no limit.
INSERT INTO plan_features (plan_id, feature_code, is_enabled, limit_value, created_at)
SELECT plan.id, feature.code, feature.enabled, feature.limit_value, now()
FROM subscription_plans AS plan
JOIN (VALUES
    ('starter', 'design_variations',       TRUE,  2),
    ('starter', 'auto_approval',           FALSE, NULL),
    ('starter', 'custom_mockup_templates', FALSE, NULL),
    ('starter', 'lifestyle_mockup',        FALSE, NULL),
    ('starter', 'listing_regenerate',      FALSE, NULL),
    ('starter', 'all_video_formats',       FALSE, NULL),
    ('starter', 'ai_video',                FALSE, NULL),
    ('starter', 'studio_keyframe',         FALSE, NULL),
    ('starter', 'daily_mockup_cap',        TRUE,  300),
    ('starter', 'daily_listing_cap',       TRUE,  100),
    ('starter', 'daily_video_cap',         TRUE,  50),
    ('pro',     'design_variations',       TRUE,  4),
    ('pro',     'auto_approval',           TRUE,  NULL),
    ('pro',     'custom_mockup_templates', TRUE,  NULL),
    ('pro',     'lifestyle_mockup',        TRUE,  NULL),
    ('pro',     'listing_regenerate',      TRUE,  NULL),
    ('pro',     'all_video_formats',       TRUE,  NULL),
    ('pro',     'ai_video',                TRUE,  NULL),
    ('pro',     'studio_keyframe',         TRUE,  NULL),
    ('pro',     'daily_mockup_cap',        TRUE,  1500),
    ('pro',     'daily_listing_cap',       TRUE,  500),
    ('pro',     'daily_video_cap',         TRUE,  250),
    ('studio',  'design_variations',       TRUE,  4),
    ('studio',  'auto_approval',           TRUE,  NULL),
    ('studio',  'custom_mockup_templates', TRUE,  NULL),
    ('studio',  'lifestyle_mockup',        TRUE,  NULL),
    ('studio',  'listing_regenerate',      TRUE,  NULL),
    ('studio',  'all_video_formats',       TRUE,  NULL),
    ('studio',  'ai_video',                TRUE,  NULL),
    ('studio',  'studio_keyframe',         TRUE,  NULL),
    ('studio',  'daily_mockup_cap',        TRUE,  1500),
    ('studio',  'daily_listing_cap',       TRUE,  500),
    ('studio',  'daily_video_cap',         TRUE,  250),
    ('vip',     'design_variations',       TRUE,  NULL),
    ('vip',     'auto_approval',           TRUE,  NULL),
    ('vip',     'custom_mockup_templates', TRUE,  NULL),
    ('vip',     'lifestyle_mockup',        TRUE,  NULL),
    ('vip',     'listing_regenerate',      TRUE,  NULL),
    ('vip',     'all_video_formats',       TRUE,  NULL),
    ('vip',     'ai_video',                TRUE,  NULL),
    ('vip',     'studio_keyframe',         TRUE,  NULL)
) AS feature (tier, code, enabled, limit_value) ON feature.tier = plan.tier
ON CONFLICT (plan_id, feature_code) DO UPDATE
SET is_enabled = EXCLUDED.is_enabled,
    limit_value = EXCLUDED.limit_value;

COMMIT;
