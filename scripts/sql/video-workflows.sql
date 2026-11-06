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
    kind varchar(20) NOT NULL CHECK (kind IN ('render','export_zip','video_quality')),
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
DROP INDEX IF EXISTS public.uq_media_job_node_active;
CREATE UNIQUE INDEX uq_media_job_node_active ON public.media_jobs(workflow_node_run_id, kind) WHERE status IN ('queued','leased');
ALTER TABLE public.media_jobs DROP CONSTRAINT IF EXISTS media_jobs_kind_check;
ALTER TABLE public.media_jobs ADD CONSTRAINT media_jobs_kind_check CHECK (kind IN ('render','export_zip','video_quality'));

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
  CHECK (type IN ('slideshow','product_showcase','lifestyle_reel','story_vertical','ai_video_scene','video_pack'));
INSERT INTO public.video_templates(id,name,type,platform,duration_seconds,aspect_ratio,resolution,effects_config,preview_video_url,is_system_template,is_active,code,template_version)
VALUES
 ('a1dc0c03-7336-4e24-a173-7a882e310001','Cinematic Product Showcase','ai_video_scene','etsy',4,'9:16','720p',
  '{"aiVideo":{"code":"cinematic_product_showcase","name":"Cinematic Product Showcase","description":"A polished product hero shot with restrained camera movement.","promptVersion":2,"basePrompt":"Create a premium e-commerce hero shot of the exact product shown in the approved reference image. The product remains the stable visual anchor while the camera performs {{CAMERA_MOVEMENT}} from {{CAMERA_ANGLE}}, using {{FRAMING}} with restrained natural parallax. {{LIGHTING}} Use realistic materials, clean commercial composition, natural shadows and polished advertising photography. Product motion is {{SUBJECT_MOTION}} and camera motion intensity is {{MOTION_INTENSITY}}. Create a {{DURATION}}-second shot.","requiresLifestyle":false,"requiresDetail":false,"allowedSettings":{"framing":["wide","medium","medium_close","close_up"],"cameraAngle":["eye_level","three_quarter","low_angle","top_down"],"cameraMovement":["locked","push_in","pull_back","orbit_left","orbit_right"],"subjectMotion":["none","minimal"],"motionIntensity":["subtle","balanced"],"lighting":["soft_studio","bright_clean","cool_modern","moody_cinematic"]},"defaults":{"templateCode":"cinematic_product_showcase","durationSeconds":4,"framing":"medium","cameraAngle":"three_quarter","cameraMovement":"push_in","subjectMotion":"minimal","motionIntensity":"subtle","lighting":"soft_studio"}}}',null,true,true,'cinematic_product_showcase',1),
 ('a1dc0c03-7336-4e24-a173-7a882e310002','Macro Detail Reveal','ai_video_scene','etsy',4,'9:16','720p',
  '{"aiVideo":{"code":"macro_detail_reveal","name":"Macro Detail Reveal","description":"A close commercial view of the printed artwork and material texture.","promptVersion":2,"basePrompt":"Create a photorealistic macro commercial shot of the visible print and surface material detail from the approved source image. The camera performs {{CAMERA_MOVEMENT}} across the marked artwork or detail region from {{CAMERA_ANGLE}}, using {{FRAMING}}. Use shallow depth of field with focus on the selected artwork and material texture. Controlled reflections reveal surface detail without obscuring the design. {{LIGHTING}} Keep the artwork clear and attached to the product surface. Camera motion intensity is {{MOTION_INTENSITY}}. Create a {{DURATION}}-second shot.","requiresLifestyle":false,"requiresDetail":true,"allowedSettings":{"framing":["macro","close_up"],"cameraAngle":["top_down","three_quarter","eye_level"],"cameraMovement":["locked","push_in","pan_left","pan_right"],"subjectMotion":["none"],"motionIntensity":["subtle","balanced"],"lighting":["soft_studio","bright_clean","moody_cinematic"]},"defaults":{"templateCode":"macro_detail_reveal","durationSeconds":4,"framing":"macro","cameraAngle":"three_quarter","cameraMovement":"pan_right","subjectMotion":"none","motionIntensity":"subtle","lighting":"soft_studio"}}}',null,true,true,'macro_detail_reveal',1),
 ('a1dc0c03-7336-4e24-a173-7a882e310003','Product On Model','ai_video_scene','etsy',4,'9:16','720p',
  '{"aiVideo":{"code":"lifestyle_in_use","name":"Product On Model","description":"Fashion videos that keep the exact product naturally presented on the model.","promptVersion":6,"basePrompt":"Create one continuous polished product-on-model fashion shot from the approved source image. Keep the model in the established pose and wearing the exact product. The camera performs {{CAMERA_MOVEMENT}} at {{CAMERA_ANGLE}} in {{FRAMING}} for the full {{DURATION}} seconds. The model subtly shifts their weight with restrained, natural body movement while the garment settles naturally across the torso. {{LIGHTING}} Use soft realistic shadows, shallow depth of field and subtle film grain. Keep the setting coherent and animate only background elements already visible in the source.","requiresLifestyle":true,"requiresDetail":false,"allowedSettings":{"framing":["wide","medium","medium_close","close_up"],"cameraAngle":["eye_level","three_quarter"],"cameraMovement":["locked","push_in","gentle_tracking"],"subjectMotion":["minimal","natural"],"motionIntensity":["subtle","balanced"],"lighting":["warm_natural","golden_hour","bright_clean"]},"defaults":{"templateCode":"lifestyle_in_use","subtemplateCode":"natural_motion","durationSeconds":4,"framing":"medium","cameraAngle":"eye_level","cameraMovement":"push_in","subjectMotion":"minimal","motionIntensity":"subtle","lighting":"warm_natural"}}}',null,true,true,'lifestyle_in_use',1),
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

-- Product On Model replaces the former Lifestyle In-Use presentation while retaining the
-- existing code so saved scenes continue to resolve without migration.
UPDATE public.video_templates
SET name = 'Product On Model',
    effects_config = jsonb_set(
      jsonb_set(
        effects_config,
        '{aiVideo}',
        (effects_config -> 'aiVideo') || jsonb_build_object(
          'name', 'Product On Model',
          'description', 'Fashion videos that keep the exact product naturally presented on the model.',
          'promptVersion', 6,
          'basePrompt', 'Create one continuous polished product-on-model fashion shot from the approved source image. Keep the model in the established pose and wearing the exact product. The camera performs {{CAMERA_MOVEMENT}} at {{CAMERA_ANGLE}} in {{FRAMING}} for the full {{DURATION}} seconds. The model subtly shifts their weight with restrained, natural body movement while the garment settles naturally across the torso. {{LIGHTING}} Use soft realistic shadows, shallow depth of field and subtle film grain. Keep the setting coherent and animate only background elements already visible in the source.',
          'allowedSettings', jsonb_build_object(
            'framing', jsonb_build_array('wide', 'medium', 'medium_close', 'close_up'),
            'cameraAngle', jsonb_build_array('eye_level', 'three_quarter'),
            'cameraMovement', jsonb_build_array('locked', 'push_in', 'gentle_tracking'),
            'subjectMotion', jsonb_build_array('minimal', 'natural'),
            'motionIntensity', jsonb_build_array('subtle', 'balanced'),
            'lighting', jsonb_build_array('warm_natural', 'golden_hour', 'bright_clean')
          ),
          'defaultSubtemplateCode', 'natural_motion',
          'subtemplates', jsonb_build_array(
            jsonb_build_object(
              'code', 'natural_motion',
              'name', 'Natural Motion',
              'description', 'A balanced fashion shot with restrained model movement and a slow camera push.',
              'basePrompt', 'Create one continuous polished product-on-model fashion shot from the approved source image. Keep the model in the established pose and wearing the exact product. The camera performs {{CAMERA_MOVEMENT}} at {{CAMERA_ANGLE}} in {{FRAMING}} for the full {{DURATION}} seconds. The model subtly shifts their weight with restrained, natural body movement while the garment settles naturally across the torso. {{LIGHTING}} Use soft realistic shadows, shallow depth of field and subtle film grain. Keep the setting coherent and animate only background elements already visible in the source.',
              'startFramePrompt', 'Create a photorealistic medium eye-level fashion keyframe. The model faces mostly forward with a slight three-quarter turn, feet planted, relaxed shoulders and natural arms that do not cover the garment or artwork. Keep the full torso, product silhouette and visible design clear in warm editorial light.',
              'defaults', jsonb_build_object('templateCode', 'lifestyle_in_use', 'subtemplateCode', 'natural_motion', 'durationSeconds', 4, 'framing', 'medium', 'cameraAngle', 'eye_level', 'cameraMovement', 'push_in', 'subjectMotion', 'minimal', 'motionIntensity', 'subtle', 'lighting', 'warm_natural')
            ),
            jsonb_build_object(
              'code', 'fabric_detail',
              'name', 'Fabric Detail Close-Up',
              'description', 'A close editorial view that reveals only garment texture and construction visible in the source.',
              'basePrompt', 'Create one continuous editorial close-up of the exact garment on the model from the approved source image. Frame a construction or surface detail that is actually visible, such as fabric texture, a seam, stitching, collar, shoulder, or print edge; do not invent missing garment features. The camera performs {{CAMERA_MOVEMENT}} at {{CAMERA_ANGLE}} in {{FRAMING}} for the full {{DURATION}} seconds. Keep the model mostly still with subtle breathing and natural fabric settling. {{LIGHTING}} Let soft light reveal real texture, use shallow depth of field, and keep the selected product detail in precise focus.',
              'startFramePrompt', 'Create a photorealistic close editorial keyframe of the model wearing the exact product, cropped around the upper torso and a real fabric or artwork detail. Keep texture, stitching, print edges, lettering and colors sharp and unchanged. Use a slight three-quarter angle, soft natural light and no invented details.',
              'defaults', jsonb_build_object('templateCode', 'lifestyle_in_use', 'subtemplateCode', 'fabric_detail', 'durationSeconds', 4, 'framing', 'close_up', 'cameraAngle', 'three_quarter', 'cameraMovement', 'push_in', 'subjectMotion', 'minimal', 'motionIntensity', 'subtle', 'lighting', 'warm_natural')
            ),
            jsonb_build_object(
              'code', 'brand_focus',
              'name', 'Brand Focus',
              'description', 'A medium torso shot that keeps visible artwork, lettering, or the garment silhouette clear.',
              'basePrompt', 'Create one continuous medium product-on-model editorial shot from the approved source image. Use torso framing and keep the garment''s visible artwork, lettering, or logo as the visual focus; if the garment has no branding, emphasize its real silhouette, color blocking, and construction instead. The camera performs {{CAMERA_MOVEMENT}} at {{CAMERA_ANGLE}} in {{FRAMING}} for the full {{DURATION}} seconds. The model makes a small confident posture adjustment while keeping the product naturally visible. {{LIGHTING}} Preserve accessories only when they already exist in the source. Use natural fabric folds, warm commercial color, shallow depth of field, and restrained film grain.',
              'startFramePrompt', 'Create a photorealistic medium-close, eye-level fashion keyframe that is nearly front-facing. Center the garment artwork, logo or silhouette and keep it flat, readable and fully unobstructed. The model stands naturally with hands outside the product area, clean commercial light and a simple coherent background.',
              'defaults', jsonb_build_object('templateCode', 'lifestyle_in_use', 'subtemplateCode', 'brand_focus', 'durationSeconds', 4, 'framing', 'medium', 'cameraAngle', 'eye_level', 'cameraMovement', 'push_in', 'subjectMotion', 'minimal', 'motionIntensity', 'subtle', 'lighting', 'golden_hour')
            ),
            jsonb_build_object(
              'code', 'profile_tracking',
              'name', 'Profile Tracking',
              'description', 'A slow side-oriented camera track around a stationary model.',
              'basePrompt', 'Create one continuous profile-tracking fashion shot from the approved source image. From the first frame, the camera tracks slowly sideways around the stationary model at a constant speed for the full {{DURATION}} seconds, with no opening hold or delayed movement. Keep the model rooted in place with feet planted: no walking, stepping or forward motion; allow only subtle breathing, a tiny weight shift without a step and minimal natural hand movement. Let the camera create a shallow three-quarter-to-profile change within the source-supported view while the garment artwork remains the visual focus and recognizable throughout. Use {{FRAMING}} at {{CAMERA_ANGLE}}. {{LIGHTING}} Use shallow depth of field and polished editorial color.',
              'startFramePrompt', 'Create a photorealistic medium eye-level fashion keyframe from a three-quarter side angle. The model stands still with feet planted, torso turned about 45 degrees, neutral head and relaxed arms away from the artwork. Keep enough garment front visible for the design and leave lateral space for an immediate sideways camera track. No walking pose.',
              'defaults', jsonb_build_object('templateCode', 'lifestyle_in_use', 'subtemplateCode', 'profile_tracking', 'durationSeconds', 4, 'framing', 'medium', 'cameraAngle', 'three_quarter', 'cameraMovement', 'gentle_tracking', 'subjectMotion', 'minimal', 'motionIntensity', 'subtle', 'lighting', 'golden_hour')
            )
          ),
          'commonPrompt', 'Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product''s visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and brief occlusion. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks, dialogue or music.'
        ),
        false
      ),
      '{aiVideo,defaults}',
      (effects_config #> '{aiVideo,defaults}') || jsonb_build_object(
        'cameraMovement', 'push_in',
        'subjectMotion', 'minimal',
        'subtemplateCode', 'natural_motion'
      ),
      false
    )
WHERE code = 'lifestyle_in_use' AND template_version = 1 AND is_system_template = true;

-- Whole-video template: scene definitions are intentionally a typed union so future packs can
-- combine AI and standard scenes without changing the saved workflow snapshot shape.
INSERT INTO public.video_templates
    (id, name, type, platform, duration_seconds, aspect_ratio, resolution, effects_config,
     preview_video_url, is_system_template, is_active, code, template_version)
VALUES
    ('a1dc0c04-7336-4e24-a173-7a882e310001', 'Product on Model', 'video_pack', 'etsy', 16, '9:16', '720p',
     '{"videoPack":{"description":"Four product-on-model shots with a shared cast and approved product mockup.","requiresCharacter":true,"requiresSharedMockup":true,"scenes":[{"id":"natural_motion","mode":"ai","durationSeconds":4,"templateCode":"lifestyle_in_use","subtemplateCode":"natural_motion","transitionIn":"cut","transitionDurationFrames":0},{"id":"profile_tracking","mode":"ai","durationSeconds":4,"templateCode":"lifestyle_in_use","subtemplateCode":"profile_tracking","transitionIn":"cut","transitionDurationFrames":0},{"id":"brand_focus","mode":"ai","durationSeconds":4,"templateCode":"lifestyle_in_use","subtemplateCode":"brand_focus","transitionIn":"cut","transitionDurationFrames":0},{"id":"fabric_detail","mode":"ai","durationSeconds":4,"templateCode":"lifestyle_in_use","subtemplateCode":"fabric_detail","transitionIn":"cut","transitionDurationFrames":0}]}}',
     null, true, true, 'product_on_model_pack', 1)
ON CONFLICT (code, template_version) WHERE code IS NOT NULL DO UPDATE SET
    name = EXCLUDED.name,
    type = EXCLUDED.type,
    duration_seconds = EXCLUDED.duration_seconds,
    aspect_ratio = EXCLUDED.aspect_ratio,
    resolution = EXCLUDED.resolution,
    effects_config = EXCLUDED.effects_config,
    is_system_template = true,
    is_active = true;

-- Version 1 made a sixteen-second sequence. Keep it for historical snapshots only;
-- every newly selectable Product on Model pack must fit the 5–15 second Etsy policy.
UPDATE public.video_templates
SET is_active = false
WHERE code = 'product_on_model_pack' AND template_version = 1;

INSERT INTO public.video_templates
    (id, name, type, platform, duration_seconds, aspect_ratio, resolution, effects_config,
     preview_video_url, is_system_template, is_active, code, template_version)
VALUES
    ('a1dc0c04-7336-4e24-a173-7a882e310002', 'Product on Model', 'video_pack', 'etsy', 12, '9:16', '720p',
     '{"videoPack":{"description":"Three product-on-model shots with a shared cast and approved product mockup.","requiresCharacter":true,"requiresSharedMockup":true,"scenes":[{"id":"natural_motion","mode":"ai","durationSeconds":4,"templateCode":"lifestyle_in_use","subtemplateCode":"natural_motion","transitionIn":"cut","transitionDurationFrames":0},{"id":"brand_focus","mode":"ai","durationSeconds":4,"templateCode":"lifestyle_in_use","subtemplateCode":"brand_focus","transitionIn":"cut","transitionDurationFrames":0},{"id":"fabric_detail","mode":"ai","durationSeconds":4,"templateCode":"lifestyle_in_use","subtemplateCode":"fabric_detail","transitionIn":"cut","transitionDurationFrames":0}]}}',
     null, true, true, 'product_on_model_pack', 2)
ON CONFLICT (code, template_version) WHERE code IS NOT NULL DO UPDATE SET
    name = EXCLUDED.name,
    type = EXCLUDED.type,
    platform = EXCLUDED.platform,
    duration_seconds = EXCLUDED.duration_seconds,
    aspect_ratio = EXCLUDED.aspect_ratio,
    resolution = EXCLUDED.resolution,
    effects_config = EXCLUDED.effects_config,
    is_system_template = true,
    is_active = true;

-- Platform-owned AI people used as shared identity references while creating video keyframes.
-- Admin management is intentionally deferred; the catalog shape already supports ordering,
-- activation and choosing exactly one default model when that UI is added.
CREATE TABLE IF NOT EXISTS public.ai_character_models (
    id uuid PRIMARY KEY,
    code varchar(80) NOT NULL,
    name varchar(100) NOT NULL,
    continent varchar(50) NOT NULL,
    gender varchar(30) NOT NULL,
    age_min smallint NOT NULL,
    age_max smallint NOT NULL,
    body_type varchar(50) NOT NULL,
    skin_tone varchar(50) NOT NULL,
    description varchar(300) NOT NULL DEFAULT '',
    cloudinary_public_id varchar(255) NOT NULL,
    image_url varchar(1000) NOT NULL,
    preview_url varchar(1000) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    is_default boolean NOT NULL DEFAULT false,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    deleted_at timestamptz,
    CONSTRAINT chk_ai_character_models_age CHECK (age_min >= 18 AND age_max >= age_min AND age_max <= 100),
    CONSTRAINT chk_ai_character_models_code CHECK (code ~ '^[a-z0-9]+(?:_[a-z0-9]+)*$'),
    CONSTRAINT chk_ai_character_models_urls CHECK (image_url <> '' AND preview_url <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_character_models_code
    ON public.ai_character_models (lower(code));
CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_character_models_default
    ON public.ai_character_models (is_default)
    WHERE is_default = true AND deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_ai_character_models_catalog
    ON public.ai_character_models (is_active, sort_order, name)
    WHERE deleted_at IS NULL;

INSERT INTO public.ai_character_models
    (id, code, name, continent, gender, age_min, age_max, body_type, skin_tone, description,
     cloudinary_public_id, image_url, preview_url, sort_order, is_default, is_active)
VALUES
    ('10000000-0000-4000-8000-000000000001', 'mai', 'Mai', 'Asia', 'Female', 18, 24, 'Slim', 'Tan',
     'Soft editorial presence with long black hair.', 'ai-characters/mai',
     'https://res.cloudinary.com/v05yzogx/image/upload/ai-characters/mai.png',
     'https://res.cloudinary.com/v05yzogx/image/upload/f_auto,q_auto,w_480/ai-characters/mai.png', 10, true, true),
    ('10000000-0000-4000-8000-000000000002', 'amina', 'Amina', 'Africa', 'Female', 25, 30, 'Athletic', 'Deep',
     'Confident, athletic and polished.', 'ai-characters/amina',
     'https://res.cloudinary.com/v05yzogx/image/upload/ai-characters/amina.png',
     'https://res.cloudinary.com/v05yzogx/image/upload/f_auto,q_auto,w_480/ai-characters/amina.png', 20, false, true),
    ('10000000-0000-4000-8000-000000000003', 'camila', 'Camila', 'South America', 'Female', 25, 30, 'Curvy', 'Warm',
     'Warm, expressive commercial fashion look.', 'ai-characters/camila',
     'https://res.cloudinary.com/v05yzogx/image/upload/ai-characters/camila.png',
     'https://res.cloudinary.com/v05yzogx/image/upload/f_auto,q_auto,w_480/ai-characters/camila.png', 30, false, true),
    ('10000000-0000-4000-8000-000000000004', 'elena', 'Elena', 'Europe', 'Female', 30, 35, 'Average', 'Fair',
     'Refined, approachable editorial look.', 'ai-characters/elena',
     'https://res.cloudinary.com/v05yzogx/image/upload/ai-characters/elena.png',
     'https://res.cloudinary.com/v05yzogx/image/upload/f_auto,q_auto,w_480/ai-characters/elena.png', 40, false, true),
    ('10000000-0000-4000-8000-000000000005', 'adrian', 'Adrian', 'Europe', 'Male', 24, 28, 'Lean athletic', 'Fair neutral',
     'Sharp high-fashion menswear presence.', 'ai-characters/adrian',
     'https://res.cloudinary.com/v05yzogx/image/upload/ai-characters/adrian.png',
     'https://res.cloudinary.com/v05yzogx/image/upload/f_auto,q_auto,w_480/ai-characters/adrian.png', 50, false, true),
    ('10000000-0000-4000-8000-000000000006', 'minh', 'Minh', 'Asia', 'Male', 22, 28, 'Slim', 'Light tan',
     'Clean, versatile contemporary style.', 'ai-characters/minh',
     'https://res.cloudinary.com/v05yzogx/image/upload/ai-characters/minh.png',
     'https://res.cloudinary.com/v05yzogx/image/upload/f_auto,q_auto,w_480/ai-characters/minh.png', 60, false, true)
ON CONFLICT (id) DO NOTHING;

COMMIT;
