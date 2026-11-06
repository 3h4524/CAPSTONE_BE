-- Plan access for the workflow steps, as agreed for Starter and Pro.
--
-- Adds two plan features an Admin can switch per plan:
--   workflow_listing  Listing content (in a workflow or on its own). Off on Starter, on on Pro.
--   workflow_publish  Publishing to Etsy and Printify, once those steps exist. Off on Starter, on on Pro.
-- and turns automatic design approval on for Starter, so both plans can choose to approve by hand
-- or let the system approve.
--
-- Free and the Pro trial are not plans in this table: their limits live in the backend's
-- UsageAllowances settings.
--
-- Additive and safe to run more than once. Review it against the target database before applying.

BEGIN;

INSERT INTO plan_features (plan_id, feature_code, is_enabled, limit_value, created_at)
SELECT plan.id, setting.feature_code, setting.is_enabled, NULL, now()
FROM subscription_plans AS plan
JOIN (VALUES
    ('starter', 'workflow_listing', FALSE),
    ('starter', 'workflow_publish', FALSE),
    ('pro',     'workflow_listing', TRUE),
    ('pro',     'workflow_publish', TRUE)
) AS setting (tier, feature_code, is_enabled) ON setting.tier = plan.tier
ON CONFLICT (plan_id, feature_code) DO NOTHING;

UPDATE plan_features AS feature
SET is_enabled = TRUE
FROM subscription_plans AS plan
WHERE feature.plan_id = plan.id
  AND plan.tier = 'starter'
  AND feature.feature_code = 'auto_approval';

COMMIT;
