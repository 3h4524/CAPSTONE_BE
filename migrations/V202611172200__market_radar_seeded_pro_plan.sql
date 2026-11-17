-- The whole Market Radar for the seeded Pro plan under whatever name it has now.
-- V202611170900 gave market_radar_full and a daily cap of 20 analyses to the plan of tier 'pro', but an Admin may rename
-- a plan's tier: on dev the seeded Pro plan (f48921c5-...) is now Creator, tier 'creator', and got nothing. Its id still
-- names it. A plan that already has these features keeps what it has.
-- Seed data is idempotent.
INSERT INTO public.plan_features (plan_id, feature_code, is_enabled, limit_value, created_at)
SELECT plan.id, feature.code, feature.is_enabled, feature.limit_value, CURRENT_TIMESTAMP
FROM public.subscription_plans plan
CROSS JOIN (VALUES
        ('market_radar_full', true, NULL::integer),
        ('daily_opportunity_analysis_cap', true, 20)
     ) AS feature(code, is_enabled, limit_value)
WHERE plan.id = 'f48921c5-e415-4442-a8bb-b132d71e8407'
  AND EXISTS (SELECT 1 FROM public.features f WHERE f.code = feature.code)
ON CONFLICT (plan_id, feature_code) DO NOTHING;
