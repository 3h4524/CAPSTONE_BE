-- A second video template (pack): four Standard scenes, no AI, each a rotating camera move on a product image.
-- Standard scenes are 1-15 s and a video is 5-15 s. The opening and closing scenes hold a second longer, so the
-- video starts on a clear hero frame and ends on one; the two in between alternate the rotation direction.
-- Between scenes a short fade (10 frames, a third of a second) keeps the changes of direction smooth.
--   1. rotate_zoom_cw   4 s  slow push in while turning clockwise (hook)
--   2. rotate_ccw       3 s  turn back counterclockwise, no zoom
--   3. rotate_cw        3 s  turn clockwise again, no zoom
--   4. rotate_zoom_ccw  4 s  counterclockwise turn with a push in, settling on the product
-- Seed data is idempotent: the unique (code, template_version) index makes a second run change nothing.
INSERT INTO public.video_templates
    (id, name, type, platform, duration_seconds, aspect_ratio, resolution, effects_config, preview_video_url,
     is_active, is_system_template, code, template_version)
VALUES
    ('a1dc0c04-7336-4e24-a173-7a882e310003', 'Rotating Showcase', 'video_pack', 'etsy', 14, '9:16', '720p',
     '{"videoPack": {"scenes": [
        {"id": "rotate_zoom_in", "mode": "standard", "durationSeconds": 4, "transitionIn": "cut", "transitionDurationFrames": 0, "standardMotionPreset": "rotate_zoom_cw"},
        {"id": "rotate_back", "mode": "standard", "durationSeconds": 3, "transitionIn": "fade", "transitionDurationFrames": 10, "standardMotionPreset": "rotate_ccw"},
        {"id": "rotate_forward", "mode": "standard", "durationSeconds": 3, "transitionIn": "fade", "transitionDurationFrames": 10, "standardMotionPreset": "rotate_cw"},
        {"id": "rotate_finale", "mode": "standard", "durationSeconds": 4, "transitionIn": "fade", "transitionDurationFrames": 10, "standardMotionPreset": "rotate_zoom_ccw"}],
       "description": "Four rotating camera moves on your product images, without AI.",
       "requiresCharacter": false, "requiresSharedMockup": false}}'::jsonb,
     NULL, true, true, 'standard_rotate_showcase', 1)
ON CONFLICT DO NOTHING;
