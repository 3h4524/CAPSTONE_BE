BEGIN;

-- Preserve existing repository features when scaffolding an older database.
ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_subject text;
ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_art_style text;
ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_mood_tone text;
ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_negative_terms text;
ALTER TABLE public.batch_job_products ADD COLUMN IF NOT EXISTS custom_instructions text;
ALTER TABLE public.mockup_templates ADD COLUMN IF NOT EXISTS user_id uuid REFERENCES public.users(id);

CREATE TABLE IF NOT EXISTS public.workflows (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES public.users(id),
    name varchar(120) NOT NULL,
    description varchar(500) NOT NULL DEFAULT '',
    definition jsonb NOT NULL,
    schema_version integer NOT NULL DEFAULT 2 CHECK (schema_version = 2),
    revision bigint NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    deleted_at timestamptz
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_workflow_owner_name ON public.workflows(user_id, lower(name)) WHERE deleted_at IS NULL;

CREATE TABLE IF NOT EXISTS public.workflow_runs (
    id uuid PRIMARY KEY,
    workflow_id uuid NOT NULL REFERENCES public.workflows(id),
    user_id uuid NOT NULL REFERENCES public.users(id),
    product_id uuid NOT NULL REFERENCES public.products(id),
    workflow_revision bigint NOT NULL,
    definition_snapshot jsonb NOT NULL,
    status varchar(40) NOT NULL CHECK (status IN ('queued','running','waiting_for_input','waiting_for_review','completed','failed','cancelled')),
    revision bigint NOT NULL DEFAULT 1,
    idempotency_key varchar(100) NOT NULL,
    request_hash varchar(64) NOT NULL,
    parent_run_id uuid REFERENCES public.workflow_runs(id),
    error_message varchar(1000),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz,
    UNIQUE(user_id, idempotency_key)
);
CREATE INDEX IF NOT EXISTS idx_workflow_runs_owner_workflow ON public.workflow_runs(user_id, workflow_id, created_at DESC);

CREATE TABLE IF NOT EXISTS public.workflow_node_runs (
    id uuid PRIMARY KEY,
    workflow_run_id uuid NOT NULL REFERENCES public.workflow_runs(id),
    node_id varchar(100) NOT NULL,
    node_type varchar(50) NOT NULL,
    status varchar(40) NOT NULL CHECK (status IN ('pending','running','waiting_for_input','waiting_for_review','succeeded','failed','skipped','cancelled')),
    attempt integer NOT NULL DEFAULT 1,
    input_snapshot jsonb NOT NULL DEFAULT '{}',
    output_snapshot jsonb NOT NULL DEFAULT '{}',
    stage varchar(50),
    progress integer NOT NULL DEFAULT 0 CHECK (progress BETWEEN 0 AND 100),
    error_message varchar(1000),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(workflow_run_id, node_id)
);

CREATE TABLE IF NOT EXISTS public.media_jobs (
    id uuid PRIMARY KEY,
    workflow_run_id uuid NOT NULL REFERENCES public.workflow_runs(id),
    workflow_node_run_id uuid NOT NULL REFERENCES public.workflow_node_runs(id),
    user_id uuid NOT NULL REFERENCES public.users(id),
    kind varchar(20) NOT NULL CHECK (kind IN ('render','export_zip')),
    status varchar(20) NOT NULL CHECK (status IN ('queued','leased','succeeded','failed','cancelled')),
    payload jsonb NOT NULL,
    attempt integer NOT NULL DEFAULT 0,
    maximum_attempts integer NOT NULL DEFAULT 3,
    lease_token uuid,
    lease_expires_at timestamptz,
    heartbeat_at timestamptz,
    available_at timestamptz NOT NULL DEFAULT now(),
    progress integer NOT NULL DEFAULT 0 CHECK (progress BETWEEN 0 AND 100),
    stage varchar(50),
    error_message varchar(1000),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_media_jobs_claim ON public.media_jobs(status, available_at, lease_expires_at);
CREATE UNIQUE INDEX IF NOT EXISTS uq_media_job_node_active ON public.media_jobs(workflow_node_run_id) WHERE status IN ('queued','leased');

ALTER TABLE public.mockup_images ALTER COLUMN design_image_id DROP NOT NULL;
ALTER TABLE public.mockup_images ALTER COLUMN mockup_template_id DROP NOT NULL;
ALTER TABLE public.mockup_images ALTER COLUMN mockup_image_url DROP NOT NULL;
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS source_type varchar(20) NOT NULL DEFAULT 'generated';
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS content_hash varchar(64);
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS metadata_revision bigint NOT NULL DEFAULT 1;
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS artwork_group_key varchar(100);
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS variant_key varchar(100);
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS role varchar(30) NOT NULL DEFAULT 'Hero';
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS regions jsonb NOT NULL DEFAULT '{}';
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS storage_version varchar(40);
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS approved_revision bigint;
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS approved_by uuid REFERENCES public.users(id);
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS approved_at timestamptz;
-- Keep the workflow runtime aligned with mockup-realism.sql and mockup-background.sql;
-- EF queries select these nullable properties even for manually uploaded source images.
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS garment_color varchar(7) NULL;
ALTER TABLE public.mockup_images ADD COLUMN IF NOT EXISTS background_color varchar(7) NULL;
UPDATE public.mockup_images SET artwork_group_key = COALESCE(design_image_id::text, product_id::text) WHERE artwork_group_key IS NULL;
ALTER TABLE public.mockup_images DROP CONSTRAINT IF EXISTS chk_mockup_source;
ALTER TABLE public.mockup_images ADD CONSTRAINT chk_mockup_source CHECK (source_type = 'uploaded' OR (source_type = 'generated' AND design_image_id IS NOT NULL AND mockup_template_id IS NOT NULL) OR source_type = 'ai_generated');

ALTER TABLE public.video_templates DROP CONSTRAINT IF EXISTS chk_video_templates_duration;
ALTER TABLE public.video_templates ADD CONSTRAINT chk_video_templates_duration CHECK (duration_seconds BETWEEN 3 AND 30);
ALTER TABLE public.video_templates ADD COLUMN IF NOT EXISTS code varchar(50);
ALTER TABLE public.video_templates ADD COLUMN IF NOT EXISTS template_version integer NOT NULL DEFAULT 1;
CREATE UNIQUE INDEX IF NOT EXISTS uq_video_template_code_version ON public.video_templates(code, template_version) WHERE code IS NOT NULL;
INSERT INTO public.video_templates(id,name,type,platform,duration_seconds,aspect_ratio,resolution,effects_config,preview_video_url,is_system_template,is_active,code,template_version)
VALUES
 ('a1dc0c01-7336-4e24-a173-7a882e310001','Product Showcase','product_showcase','etsy',12,'9:16','1080x1920','{}',null,true,true,'product_showcase',1),
 ('a1dc0c01-7336-4e24-a173-7a882e310002','Design Detail','product_showcase','etsy',12,'9:16','1080x1920','{}',null,true,true,'design_detail',1),
 ('a1dc0c01-7336-4e24-a173-7a882e310003','Variant Showcase','product_showcase','etsy',12,'9:16','1080x1920','{}',null,true,true,'variant_showcase',1),
 ('a1dc0c02-7336-4e24-a173-7a882e310001','Product Showcase','product_showcase','etsy',12,'9:16','1080x1920','{"description":"A balanced product-first video that works with one or more approved mockups.","defaultTransition":"none","defaultMotionPreset":"varied","requirements":{"minimumAssets":1,"requiresDetail":false,"minimumVariants":0}}','/video-templates/product-showcase-v2.mp4',true,true,'product_showcase',2),
 ('a1dc0c02-7336-4e24-a173-7a882e310002','Design Detail','product_showcase','etsy',12,'9:16','1080x1920','{"description":"Moves from the complete product to a close look at the artwork.","defaultTransition":"none","defaultMotionPreset":"varied","requirements":{"minimumAssets":1,"requiresDetail":true,"minimumVariants":0}}','/video-templates/design-detail-v2.mp4',true,true,'design_detail',2),
 ('a1dc0c02-7336-4e24-a173-7a882e310003','Variant Showcase','product_showcase','etsy',12,'9:16','1080x1920','{"description":"Shows distinct product variants before returning to the strongest view.","defaultTransition":"none","defaultMotionPreset":"varied","requirements":{"minimumAssets":2,"requiresDetail":false,"minimumVariants":2}}','/video-templates/variant-showcase-v2.mp4',true,true,'variant_showcase',2)
ON CONFLICT(id) DO UPDATE SET aspect_ratio=EXCLUDED.aspect_ratio, resolution=EXCLUDED.resolution;

-- AI video prompt catalog: the provider-independent prompt/settings definition is versioned in
-- video_templates.effects_config. Keep these six built-ins in sync with AiVideoPromptCompiler.
-- Their rows have a type of their own, which the original schema did not allow.
ALTER TABLE public.video_templates DROP CONSTRAINT IF EXISTS chk_video_templates_type;
ALTER TABLE public.video_templates ADD CONSTRAINT chk_video_templates_type
  CHECK (type IN ('slideshow','product_showcase','lifestyle_reel','story_vertical','ai_video_scene'));
INSERT INTO public.video_templates(id,name,type,platform,duration_seconds,aspect_ratio,resolution,effects_config,preview_video_url,is_system_template,is_active,code,template_version)
VALUES
 ('a1dc0c03-7336-4e24-a173-7a882e310001','Cinematic Product Showcase','ai_video_scene','etsy',4,'9:16','720p',
  '{"aiVideo":{"code":"cinematic_product_showcase","name":"Cinematic Product Showcase","description":"A polished product hero shot with restrained camera movement.","promptVersion":2,"basePrompt":"Create a premium e-commerce hero shot of the exact product shown in the approved reference image. The product remains the stable visual anchor while the camera performs {{CAMERA_MOVEMENT}} from {{CAMERA_ANGLE}}, using {{FRAMING}} with restrained natural parallax. {{LIGHTING}} Use realistic materials, clean commercial composition, natural shadows and polished advertising photography. Product motion is {{SUBJECT_MOTION}} and camera motion intensity is {{MOTION_INTENSITY}}. Create a {{DURATION}}-second shot.","requiresLifestyle":false,"requiresDetail":false,"allowedSettings":{"framing":["wide","medium","medium_close","close_up"],"cameraAngle":["eye_level","three_quarter","low_angle","top_down"],"cameraMovement":["locked","push_in","pull_back","orbit_left","orbit_right"],"subjectMotion":["none","minimal"],"motionIntensity":["subtle","balanced"],"lighting":["soft_studio","bright_clean","cool_modern","moody_cinematic"]},"defaults":{"templateCode":"cinematic_product_showcase","durationSeconds":4,"framing":"medium","cameraAngle":"three_quarter","cameraMovement":"push_in","subjectMotion":"minimal","motionIntensity":"subtle","lighting":"soft_studio"}}}',null,true,true,'cinematic_product_showcase',1),
 ('a1dc0c03-7336-4e24-a173-7a882e310002','Macro Detail Reveal','ai_video_scene','etsy',4,'9:16','720p',
  '{"aiVideo":{"code":"macro_detail_reveal","name":"Macro Detail Reveal","description":"A close commercial view of the printed artwork and material texture.","promptVersion":2,"basePrompt":"Create a photorealistic macro commercial shot of the visible print and surface material detail from the approved source image. The camera performs {{CAMERA_MOVEMENT}} across the marked artwork or detail region from {{CAMERA_ANGLE}}, using {{FRAMING}}. Use shallow depth of field with focus on the selected artwork and material texture. Controlled reflections reveal surface detail without obscuring the design. {{LIGHTING}} Keep the artwork clear and attached to the product surface. Camera motion intensity is {{MOTION_INTENSITY}}. Create a {{DURATION}}-second shot.","requiresLifestyle":false,"requiresDetail":true,"allowedSettings":{"framing":["macro","close_up"],"cameraAngle":["top_down","three_quarter","eye_level"],"cameraMovement":["locked","push_in","pan_left","pan_right"],"subjectMotion":["none"],"motionIntensity":["subtle","balanced"],"lighting":["soft_studio","bright_clean","moody_cinematic"]},"defaults":{"templateCode":"macro_detail_reveal","durationSeconds":4,"framing":"macro","cameraAngle":"three_quarter","cameraMovement":"pan_right","subjectMotion":"none","motionIntensity":"subtle","lighting":"soft_studio"}}}',null,true,true,'macro_detail_reveal',1),
 ('a1dc0c03-7336-4e24-a173-7a882e310003','Lifestyle In-Use','ai_video_scene','etsy',4,'9:16','720p',
  '{"aiVideo":{"code":"lifestyle_in_use","name":"Lifestyle In-Use","description":"A natural lifestyle shot of an adult already visible in the approved source image.","promptVersion":2,"basePrompt":"Create a polished lifestyle commercial shot of the adult already visible in the approved source image, naturally wearing or using the exact product. The person performs {{PRODUCT_ACTION}} while the product and its artwork remain unobstructed and clearly readable. The camera performs {{CAMERA_MOVEMENT}} at {{CAMERA_ANGLE}} in {{FRAMING}}. {{LIGHTING}} Use natural body movement, realistic fabric physics and an authentic environment. Do not use a pose change that hides the product. Create a {{DURATION}}-second shot.","requiresLifestyle":true,"requiresDetail":false,"allowedSettings":{"framing":["wide","medium","medium_close"],"cameraAngle":["eye_level","three_quarter"],"cameraMovement":["locked","push_in","gentle_tracking"],"subjectMotion":["minimal","natural"],"motionIntensity":["subtle","balanced"],"lighting":["warm_natural","golden_hour","bright_clean"]},"defaults":{"templateCode":"lifestyle_in_use","durationSeconds":4,"framing":"medium","cameraAngle":"eye_level","cameraMovement":"gentle_tracking","subjectMotion":"natural","motionIntensity":"subtle","lighting":"warm_natural"}}}',null,true,true,'lifestyle_in_use',1),
 ('a1dc0c03-7336-4e24-a173-7a882e310004','Creator POV / UGC','ai_video_scene','etsy',4,'9:16','720p',
  '{"aiVideo":{"code":"creator_pov_ugc","name":"Creator POV / UGC","description":"An authentic creator-style product demonstration with a phone-camera feel.","promptVersion":2,"basePrompt":"Create an authentic creator-style product demonstration featuring the adult already visible in the approved source image. Use a realistic phone-camera aesthetic in {{FRAMING}}, {{CAMERA_ANGLE}} composition. The camera uses {{CAMERA_MOVEMENT}} with controlled micro-movement. The creator naturally presents the exact product toward the camera without covering its artwork. {{LIGHTING}} Maintain candid UGC realism, accurate anatomy and stable product details. There is no dialogue, lip-sync or exaggerated performance. Create a {{DURATION}}-second shot.","requiresLifestyle":true,"requiresDetail":false,"allowedSettings":{"framing":["medium_close","close_up"],"cameraAngle":["eye_level","three_quarter"],"cameraMovement":["locked","push_in","subtle_handheld"],"subjectMotion":["minimal","natural"],"motionIntensity":["subtle","balanced"],"lighting":["bright_clean","warm_natural"]},"defaults":{"templateCode":"creator_pov_ugc","durationSeconds":4,"framing":"close_up","cameraAngle":"eye_level","cameraMovement":"subtle_handheld","subjectMotion":"natural","motionIntensity":"subtle","lighting":"bright_clean"}}}',null,true,true,'creator_pov_ugc',1),
 ('a1dc0c03-7336-4e24-a173-7a882e310005','Everyday Story Moment','ai_video_scene','etsy',4,'9:16','720p',
  '{"aiVideo":{"code":"everyday_story_moment","name":"Everyday Story Moment","description":"A warm, grounded everyday scene centered on the product.","promptVersion":2,"basePrompt":"Create a warm, emotionally grounded everyday moment centered on the adult already visible in the approved source image and the exact product. The person performs {{PRODUCT_ACTION}} while the camera observes with {{CAMERA_MOVEMENT}} from {{CAMERA_ANGLE}}, using {{FRAMING}}. {{LIGHTING}} Use a natural expression, restrained movement and intimate commercial storytelling. The product remains clearly identifiable throughout. Create a {{DURATION}}-second shot.","requiresLifestyle":true,"requiresDetail":false,"allowedSettings":{"framing":["wide","medium","medium_close"],"cameraAngle":["eye_level","three_quarter"],"cameraMovement":["locked","gentle_tracking","push_in"],"subjectMotion":["minimal","natural"],"motionIntensity":["subtle","balanced"],"lighting":["warm_natural","golden_hour","moody_cinematic"]},"defaults":{"templateCode":"everyday_story_moment","durationSeconds":4,"framing":"medium","cameraAngle":"eye_level","cameraMovement":"gentle_tracking","subjectMotion":"natural","motionIntensity":"subtle","lighting":"golden_hour"}}}',null,true,true,'everyday_story_moment',1),
 ('a1dc0c03-7336-4e24-a173-7a882e310006','Signature Product Reveal','ai_video_scene','etsy',4,'9:16','720p',
  '{"aiVideo":{"code":"signature_product_reveal","name":"Signature Product Reveal","description":"A restrained cinematic reveal that finishes on a clean hero frame.","promptVersion":2,"basePrompt":"Create a high-impact cinematic reveal of the exact product already shown in the approved source image. Begin from the supplied composition. The camera performs {{CAMERA_MOVEMENT}} from {{CAMERA_ANGLE}}, using {{FRAMING}}, while a restrained lighting transition reveals the product silhouette and artwork. Finish on a clean, stable hero frame suitable for an opening hook or closing shot. {{LIGHTING}} Avoid materialization, particles, smoke, liquid effects and product transformation. Product motion is {{SUBJECT_MOTION}}; camera motion intensity is {{MOTION_INTENSITY}}. Create a {{DURATION}}-second shot.","requiresLifestyle":false,"requiresDetail":false,"allowedSettings":{"framing":["medium","medium_close","close_up"],"cameraAngle":["three_quarter","low_angle","eye_level"],"cameraMovement":["locked","push_in","orbit_left","orbit_right"],"subjectMotion":["none","minimal"],"motionIntensity":["subtle","balanced"],"lighting":["moody_cinematic","soft_studio","cool_modern"]},"defaults":{"templateCode":"signature_product_reveal","durationSeconds":4,"framing":"medium_close","cameraAngle":"three_quarter","cameraMovement":"orbit_left","subjectMotion":"minimal","motionIntensity":"balanced","lighting":"moody_cinematic"}}}',null,true,true,'signature_product_reveal',1)
ON CONFLICT (code, template_version) WHERE code IS NOT NULL DO UPDATE SET
 name = EXCLUDED.name, duration_seconds = EXCLUDED.duration_seconds, effects_config = EXCLUDED.effects_config,
 is_system_template = true, is_active = true;

UPDATE public.video_templates
SET effects_config = jsonb_set(
  jsonb_set(effects_config, '{aiVideo,commonPrompt}', to_jsonb(
    'Use the approved source image as the primary visual reference. Keep the product recognizable by preserving its visible shape, main color, material character, proportions, and the placement of its artwork, logo and readable text. Let fabric or other materials, lighting, shadows, background and camera perspective move naturally when appropriate to the scene. Keep the product coherent over time; avoid duplicate products or conspicuous details that conflict with the source image. Create one continuous shot without cuts. Avoid unrelated text, watermarks and extra products. No dialogue, speech, singing or music; use quiet ambient room tone only.'::text), true),
  '{aiVideo,promptVersion}', '2'::jsonb, true)
WHERE code IN ('cinematic_product_showcase','macro_detail_reveal','lifestyle_in_use','creator_pov_ugc','everyday_story_moment','signature_product_reveal')
  AND template_version = 1 AND is_system_template = true;

UPDATE public.video_templates
SET effects_config = jsonb_set(
  jsonb_set(
    jsonb_set(
      jsonb_set(effects_config, '{aiVideo,promptVersion}', '3'::jsonb, true),
      '{aiVideo,basePrompt}', to_jsonb('Create a premium e-commerce hero shot of the exact product shown in the approved image supplied for this scene. Keep the product clear and stable while the camera performs a very slow, smooth {{CAMERA_MOVEMENT}} from {{CAMERA_ANGLE}}, using {{FRAMING}}. Move only a few percent of the frame over the complete shot; no sudden zoom, orbit, roll or cuts. {{LIGHTING}} Use realistic materials, clean commercial composition and natural shadows. Product motion is {{SUBJECT_MOTION}} and camera motion intensity is {{MOTION_INTENSITY}}. Create a {{DURATION}}-second shot.'::text), true),
    '{aiVideo,allowedSettings,cameraMovement}', '["locked","push_in","pull_back","pan_left","pan_right"]'::jsonb, true),
  '{aiVideo,defaults,cameraMovement}', to_jsonb('push_in'::text), true)
WHERE code = 'cinematic_product_showcase' AND template_version = 1 AND is_system_template = true;

UPDATE public.video_templates
SET effects_config = jsonb_set(
  jsonb_set(effects_config, '{aiVideo,promptVersion}', '4'::jsonb, true),
  '{aiVideo,basePrompt}', to_jsonb('Create a premium e-commerce hero shot of the exact product shown in the approved image supplied for this scene. Keep the product clear and stable while the camera uses {{CAMERA_MOVEMENT}} from {{CAMERA_ANGLE}}, using {{FRAMING}}. {{LIGHTING}} Use realistic materials, clean commercial composition and natural shadows. Product motion is {{SUBJECT_MOTION}}. Create a {{DURATION}}-second shot.'::text), true)
WHERE code = 'cinematic_product_showcase' AND template_version = 1 AND is_system_template = true;

-- Camera-first prompt revisions: product stability applies to identity, while the selected camera
-- movement remains a required visible action for Showcase and Macro Detail scenes.
UPDATE public.video_templates
SET effects_config = jsonb_set(
  jsonb_set(effects_config, '{aiVideo,promptVersion}', '5'::jsonb, true),
  '{aiVideo,basePrompt}', to_jsonb('Create one continuous premium e-commerce hero shot from the approved source image. The selected {{CAMERA_MOVEMENT}} is the primary visible action: it begins in the first frame and continues smoothly for the full {{DURATION}} seconds from {{CAMERA_ANGLE}}, using {{FRAMING}}. Keep the product itself still and preserve its exact identity and artwork while the camera moves. {{LIGHTING}} Use realistic materials, coherent background parallax, clean commercial composition and natural shadows. Camera motion intensity is {{MOTION_INTENSITY}}.'::text), true)
WHERE code = 'cinematic_product_showcase' AND template_version = 1 AND is_system_template = true;

UPDATE public.video_templates
SET effects_config = jsonb_set(
  jsonb_set(effects_config, '{aiVideo,promptVersion}', '3'::jsonb, true),
  '{aiVideo,basePrompt}', to_jsonb('Create one continuous photorealistic macro commercial shot from the approved detail image. The selected {{CAMERA_MOVEMENT}} is the primary visible action and continues smoothly across the artwork for the full {{DURATION}} seconds from {{CAMERA_ANGLE}}, using {{FRAMING}}. Keep the focus plane locked to the printed surface so fine material texture, print edges and all visible lettering remain sharp and readable while the camera moves. {{LIGHTING}} Use controlled reflections without glare or focus hunting. Keep the artwork physically attached to the product. Camera motion intensity is {{MOTION_INTENSITY}}.'::text), true)
WHERE code = 'macro_detail_reveal' AND template_version = 1 AND is_system_template = true;

ALTER TABLE public.promo_videos DROP CONSTRAINT IF EXISTS chk_promo_videos_duration;
ALTER TABLE public.promo_videos ADD CONSTRAINT chk_promo_videos_duration CHECK (video_duration_seconds BETWEEN 3 AND 30);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS mode varchar(30) NOT NULL DEFAULT 'standard';
ALTER TABLE public.promo_videos DROP CONSTRAINT IF EXISTS promo_videos_mode_check;
ALTER TABLE public.promo_videos DROP CONSTRAINT IF EXISTS chk_promo_videos_mode;
ALTER TABLE public.promo_videos ADD CONSTRAINT chk_promo_videos_mode CHECK (mode IN ('standard', 'ai_shot'));
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS series_id uuid;
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS version_number integer NOT NULL DEFAULT 1;
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS workflow_run_id uuid REFERENCES public.workflow_runs(id);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS fingerprint varchar(64);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS template_version integer NOT NULL DEFAULT 1;
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS config_snapshot jsonb NOT NULL DEFAULT '{}';
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS qa_result jsonb NOT NULL DEFAULT '{}';
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS review_revision bigint NOT NULL DEFAULT 1;
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS approved_by uuid REFERENCES public.users(id);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS approved_at timestamptz;
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS thumbnail_storage_key varchar(500);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS storage_version varchar(40);
ALTER TABLE public.promo_videos ADD COLUMN IF NOT EXISTS thumbnail_storage_version varchar(40);
CREATE UNIQUE INDEX IF NOT EXISTS uq_promo_video_series_version ON public.promo_videos(series_id,version_number) WHERE series_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_promo_video_fingerprint ON public.promo_videos(product_id,fingerprint);

ALTER TABLE public.promo_video_scenes ADD COLUMN IF NOT EXISTS generation_strategy varchar(30) NOT NULL DEFAULT 'standard';
ALTER TABLE public.promo_video_scenes ADD COLUMN IF NOT EXISTS source_revision bigint;
ALTER TABLE public.promo_video_scenes ADD COLUMN IF NOT EXISTS source_hash varchar(64);
ALTER TABLE public.promo_video_scenes ADD COLUMN IF NOT EXISTS scene_config jsonb NOT NULL DEFAULT '{}';
ALTER TABLE public.promo_video_scenes ADD COLUMN IF NOT EXISTS warnings jsonb NOT NULL DEFAULT '[]';
ALTER TABLE public.export_package_items ADD COLUMN IF NOT EXISTS promo_video_id uuid REFERENCES public.promo_videos(id);
ALTER TABLE public.export_packages ADD COLUMN IF NOT EXISTS workflow_run_id uuid REFERENCES public.workflow_runs(id);
ALTER TABLE public.export_packages ADD COLUMN IF NOT EXISTS manifest jsonb NOT NULL DEFAULT '{}';

-- Keep the Showcase default still while preserving any explicit scene-level selection.
UPDATE public.video_templates
SET effects_config = jsonb_set(effects_config, '{aiVideo,defaults,subjectMotion}', '"none"'::jsonb, true)
WHERE code = 'cinematic_product_showcase' AND template_version = 1 AND is_system_template = true;

COMMIT;
