-- Retire three AI video scene templates. Each asks the video model to invent what the approved images do not show:
-- a product held toward the camera (creator_pov_ugc), a new everyday setting (everyday_story_moment), and a
-- dark-to-light reveal on a white mock-up (signature_product_reveal). The application already leaves them out of its
-- catalog (AiVideoPromptCompiler.RetiredTemplateCodes); this keeps the stored rows in step. The rows are kept, not
-- deleted, so videos already made with them keep their history, and re-enabling one is a single update.
UPDATE public.video_templates
SET is_active = false,
    updated_at = CURRENT_TIMESTAMP
WHERE type = 'ai_video_scene'
  AND code IN ('creator_pov_ugc', 'everyday_story_moment', 'signature_product_reveal');
