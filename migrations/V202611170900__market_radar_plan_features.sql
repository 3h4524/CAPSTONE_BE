-- Market Radar on the plans:
--   market_radar_full               the whole radar: every opportunity, its niche's competitors, keywords, charts,
--                                   action plan and risks, an on-demand refresh and the AI analysis. Without it a
--                                   Seller sees the top three opportunities and the niche verdicts.
--   daily_opportunity_analysis_cap  the most AI analyses of opportunities one Seller asks for in a day (UTC); an
--                                   analysis whose evidence has not changed is reused and does not count.
-- Pro, Studio and Vip get the whole radar; Pro and Studio a daily cap, Vip none. Starter and Free do not.
-- Seed data is idempotent.
INSERT INTO public.features
    (code, label, description, group_name, kind, unit, default_limit, trial_available, is_active, sort_order)
VALUES
    ('market_radar_full', 'Full Market Radar',
     'Every opportunity of the radar with its niche''s competitors, keywords, charts, action plan and risks, an on-demand refresh and the AI analysis. Without it a Seller sees the top three opportunities.',
     'Market Radar', 'flag', NULL, NULL, true, true, 90),
    ('daily_opportunity_analysis_cap', 'Daily opportunity analyses',
     'Most AI analyses of opportunities a Seller asks for per day. An analysis whose evidence has not changed is reused and does not count. Leave empty for no cap.',
     'Market Radar', 'limit', 'per day', 20, true, true, 91)
ON CONFLICT DO NOTHING;

INSERT INTO public.plan_features (plan_id, feature_code, is_enabled, limit_value, created_at)
SELECT plan.id, feature.code, feature.is_enabled, feature.limit_value, CURRENT_TIMESTAMP
FROM public.subscription_plans plan
JOIN (VALUES
        ('pro', 'market_radar_full', true, NULL::integer),
        ('pro', 'daily_opportunity_analysis_cap', true, 20),
        ('studio', 'market_radar_full', true, NULL::integer),
        ('studio', 'daily_opportunity_analysis_cap', true, 50),
        ('vip', 'market_radar_full', true, NULL::integer),
        ('starter', 'market_radar_full', false, NULL::integer)
     ) AS feature(plan_tier, code, is_enabled, limit_value)
  ON feature.plan_tier = plan.tier
ON CONFLICT (plan_id, feature_code) DO NOTHING;
