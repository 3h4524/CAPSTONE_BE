-- Two plan features for plans whose Ideogram images (designs, lifestyle mockups, model photos) are made on the
-- platform's own keys (SystemApiKeys__Ideogram__Keys / IDEOGRAM_API_KEY in the API's environment) instead of the Seller's key:
--   system_image_keys       the flag that switches it on; left out of the free trial, as the platform pays for it
--   daily_design_image_cap  the most AI images one Seller makes in a day (UTC) on such a plan
-- No plan gets them here: an Admin turns them on per plan. Seed data is idempotent.
INSERT INTO public.features
    (code, label, description, group_name, kind, unit, default_limit, trial_available, is_active, sort_order)
VALUES
    ('system_image_keys', 'Ideogram images on the platform''s keys',
     'Make Ideogram design images, lifestyle mockups and model photos with the platform''s own keys, so the Seller needs no Ideogram key of their own.',
     'Design images', 'flag', NULL, NULL, false, true, 22),
    ('daily_design_image_cap', 'Daily AI image safety cap',
     'Most AI images (designs, lifestyle mockups, model photos) made with the platform''s keys per day. Leave empty for no cap.',
     'Safety caps', 'limit', 'per day', 200, true, true, 115)
ON CONFLICT DO NOTHING;
