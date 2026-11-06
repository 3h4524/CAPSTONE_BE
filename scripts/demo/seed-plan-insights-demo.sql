-- What the demo customers made, so the Admin's Plan Insights tab and the Revenue page's usage
-- signals have something real to compare: designs, mockups, lifestyle mockups, keyframes,
-- listings, videos, exports, custom templates and automatic approval.
--
-- DEMO DATABASE ONLY. Run it after seed-revenue-demo.sql, which creates the customers and removes
-- what an earlier run of this script added.
--
-- Every customer stays inside what their plan allows at the time (PlanEntitlements, UsageAllowances
-- and plan_features):
--   * Free: 1 product and 1 design per run, up to 3 mockups per run; per month at most 7 designs,
--     6 mockups, 2 standard videos and 5 listings. About a quarter reach a monthly limit.
--   * Pro trial: up to 3 products per run, 10 products, 40 designs, 100 mockups, 10 videos and
--     20 listings; no AI video and no studio keyframes. About a fifth reach the mockup limit.
--   * Starter: 2 design variations, standard portrait videos, no listings and no AI tools.
--     Three customers reach the 300 mockups a day cap on 3 days of their last 30.
--   * Pro: 4 variations and the AI tools. AI video is used by about a fifth of Pro
--     customers and fails about a fifth of the time, so it shows as underused and failing.
--   * Customers who stopped paying fall in each of Plan Insights' leave groups: about 30% never
--     made a design or a mockup, 15% met many failures, 15% of those on Starter kept hitting the
--     daily cap, 30% slowed down before leaving and the rest used the plan steadily.
--     Of those who stay, 10% made nothing.
-- setseed() makes every run produce the same rows.

\set ON_ERROR_STOP on

BEGIN;

-- One product and what was made for it.
CREATE FUNCTION pg_temp.demo_product(
    p_user uuid, p_batch uuid, p_at timestamptz,
    p_designs int,            -- design variations, 1-4
    p_mockups int,            -- generated mockups in all, spread over the designs
    p_auto_approve boolean,
    p_lifestyle boolean, p_keyframe boolean, p_uploaded boolean,
    p_listing boolean, p_listing_regenerate boolean,
    p_video text,             -- NULL, 'portrait' or 'landscape'
    p_ai_video boolean, p_export boolean,
    p_fail_rate numeric DEFAULT 0.05)  -- share of image calls that fail and are retried
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    v_product  uuid := gen_random_uuid();
    v_prompt   uuid := gen_random_uuid();
    v_job      uuid := gen_random_uuid();
    v_design   uuid;
    v_usage    uuid;
    v_listing  uuid;
    v_designs  uuid[] := '{}';
    v_template uuid;
    v_workflow uuid;
    v_run      uuid;
    v_type     text := (ARRAY['tshirt','tshirt','tshirt','hoodie','mug','poster','tote_bag'])[1 + floor(random() * 7)::int];
    v_names    text[] := ARRAY['Retro sunset','Cozy cat','Coffee first','Mountain trail','Bookworm club','Plant mom',
                               'Vintage moto','Ocean waves','Lunar phases','Golden retriever','Gamer mode','Teacher life',
                               'Camping crew','Minimal floral','Hello autumn','Bee kind','Dad joke','Desert cactus'];
BEGIN
    INSERT INTO batch_jobs (id, user_id, name, source_file_type, batch_id, job_type, status, config, created_at)
    VALUES (v_job, p_user, 'Design run', 'manual', p_batch, 'design_generation', 'completed',
            jsonb_build_object('requireApproval', NOT p_auto_approve), p_at);
    INSERT INTO products (id, user_id, name, product_type, input_description, batch_id, processing_status, created_at)
    VALUES (v_product, p_user,
            v_names[1 + floor(random() * array_length(v_names, 1))::int] || ' ' || replace(v_type, '_', ' ')
                || ' #' || (SELECT count(*) + 1 FROM products WHERE batch_id = p_batch),  -- names are unique in a batch
            v_type, 'Demo product', p_batch, 'approved', p_at);
    INSERT INTO ai_prompts (id, product_id, original_description, system_prompt, generated_prompt, created_at)
    VALUES (v_prompt, v_product, 'Demo product', 'Demo', 'Demo prompt', p_at);

    FOR j IN 1..p_designs LOOP
        IF random() < p_fail_rate THEN
            INSERT INTO api_usage_records (user_id, product_id, batch_job_id, provider, feature, request_units, cost_usd, status, created_at)
            VALUES (p_user, v_product, v_job, 'gemini', 'image_generation', 1, 0, 'failed', p_at + interval '1 minute');
        END IF;
        v_usage := gen_random_uuid();
        INSERT INTO api_usage_records (id, user_id, product_id, batch_job_id, provider, feature, request_units, cost_usd, status, created_at)
        VALUES (v_usage, p_user, v_product, v_job, 'gemini', 'image_generation', 1, 0.039, 'success', p_at + interval '2 minutes');
        v_design := gen_random_uuid();
        INSERT INTO design_images (id, product_id, ai_prompt_id, api_usage_record_id, image_generator_model, storage_key, image_url,
                                   image_width_px, image_height_px, file_format, file_size_mb, generation_time_seconds,
                                   approval_status, variation_index, created_at)
        VALUES (v_design, v_product, v_prompt, v_usage, 'gemini-3.1-flash-image', 'demo/' || v_design,
                'https://example.invalid/' || v_design || '.png', 1024, 1024, 'png', 1.2, 8.5,
                CASE WHEN p_auto_approve OR random() < 0.8 THEN 'approved' ELSE 'pending' END, j, p_at + interval '3 minutes');
        v_designs := v_designs || v_design;
    END LOOP;

    FOR k IN 1..p_mockups LOOP
        SELECT id INTO v_template FROM mockup_templates WHERE user_id IS NULL ORDER BY random() LIMIT 1;
        INSERT INTO mockup_images (product_id, design_image_id, mockup_template_id, storage_key, mockup_width_px, mockup_height_px,
                                   source_type, approval_status, created_at)
        VALUES (v_product, v_designs[1 + (k - 1) % array_length(v_designs, 1)], v_template, 'demo/mockup', 1600, 1600,
                'generated', CASE WHEN random() < 0.85 THEN 'approved' ELSE 'pending' END, p_at + interval '10 minutes');
    END LOOP;

    IF p_uploaded THEN
        INSERT INTO mockup_images (product_id, storage_key, mockup_width_px, mockup_height_px, source_type, approval_status, created_at)
        VALUES (v_product, 'demo/upload', 1600, 1600, 'uploaded', 'approved', p_at + interval '12 minutes');
    END IF;

    IF p_lifestyle THEN
        v_usage := gen_random_uuid();
        INSERT INTO api_usage_records (id, user_id, product_id, provider, feature, request_units, cost_usd, status, created_at)
        VALUES (v_usage, p_user, v_product, 'gemini', 'mockup', 1, 0.039, 'success', p_at + interval '15 minutes');
        INSERT INTO mockup_images (product_id, api_usage_record_id, storage_key, mockup_width_px, mockup_height_px, source_type,
                                   role, approval_status, created_at)
        VALUES (v_product, v_usage, 'demo/lifestyle', 1024, 1536, 'uploaded', 'Lifestyle', 'approved', p_at + interval '15 minutes');
    END IF;

    IF p_keyframe THEN
        INSERT INTO mockup_images (product_id, storage_key, mockup_width_px, mockup_height_px, source_type, approval_status, created_at)
        VALUES (v_product, 'demo/keyframe', 1080, 1920, 'ai_generated', 'approved', p_at + interval '16 minutes');
    END IF;

    IF p_listing THEN
        -- Listings run on the platform key, with a few failures.
        IF random() < 0.04 THEN
            INSERT INTO api_usage_records (user_id, product_id, provider, feature, request_units, cost_usd, status, created_at)
            VALUES (p_user, v_product, 'gemini', 'listing', 1, 0, 'failed', p_at + interval '19 minutes');
        END IF;
        v_usage := gen_random_uuid();
        INSERT INTO api_usage_records (id, user_id, product_id, provider, feature, request_units, cost_usd, status, created_at)
        VALUES (v_usage, p_user, v_product, 'gemini', 'listing', 1, 0.0004, 'success', p_at + interval '20 minutes');
        IF p_listing_regenerate THEN
            v_listing := gen_random_uuid();
            INSERT INTO listing_contents (id, product_id, ai_model_used, model_version, generation_time_seconds, created_at)
            VALUES (v_listing, v_product, 'gemini', 'gemini-3.5-flash', 4.2, p_at + interval '20 minutes');
            INSERT INTO listing_generation_history (listing_content_id, api_usage_record_id, generation_number, prompt_used,
                                                    raw_ai_response, user_action, created_at)
            VALUES (v_listing, v_usage, 1, 'Demo', '{}', 'regenerated', p_at + interval '20 minutes');
            v_usage := gen_random_uuid();
            INSERT INTO api_usage_records (id, user_id, product_id, provider, feature, request_units, cost_usd, status, created_at)
            VALUES (v_usage, p_user, v_product, 'gemini', 'listing', 1, 0.0004, 'success', p_at + interval '22 minutes');
            INSERT INTO listing_generation_history (listing_content_id, api_usage_record_id, generation_number, prompt_used,
                                                    raw_ai_response, user_action, created_at)
            VALUES (v_listing, v_usage, 2, 'Demo', '{}', 'accepted', p_at + interval '22 minutes');
        END IF;
    END IF;

    IF p_video IS NOT NULL THEN
        INSERT INTO promo_videos (product_id, video_template_id, video_duration_seconds, video_resolution, file_format, aspect_ratio,
                                  platform_target, status, approval_status, mode, created_at)
        VALUES (v_product, (SELECT id FROM video_templates ORDER BY random() LIMIT 1), 12,
                CASE p_video WHEN 'landscape' THEN '1920x1080' ELSE '1080x1920' END, 'mp4',
                CASE p_video WHEN 'landscape' THEN '16:9' ELSE '9:16' END, 'etsy',
                CASE WHEN random() < 0.04 THEN 'failed' ELSE 'completed' END, 'approved', 'standard', p_at + interval '30 minutes');
    END IF;

    IF p_ai_video THEN
        INSERT INTO promo_videos (product_id, video_template_id, video_duration_seconds, video_resolution, file_format, aspect_ratio,
                                  platform_target, status, approval_status, mode, created_at)
        VALUES (v_product, (SELECT id FROM video_templates ORDER BY random() LIMIT 1), 16, '720x1280', 'mp4', '9:16', 'etsy',
                CASE WHEN random() < 0.22 THEN 'failed' ELSE 'completed' END, 'pending', 'ai_shot', p_at + interval '35 minutes');
    END IF;

    -- A ZIP export comes out of a workflow run, which is how Plan Insights counts exported products.
    IF p_export THEN
        SELECT id INTO v_workflow FROM workflows WHERE user_id = p_user ORDER BY created_at LIMIT 1;
        IF v_workflow IS NULL THEN
            v_workflow := gen_random_uuid();
            INSERT INTO workflows (id, user_id, name, definition, created_at)
            VALUES (v_workflow, p_user, 'Etsy launch', '{"version":2,"nodes":[],"edges":[]}', p_at);
        END IF;
        v_run := gen_random_uuid();
        INSERT INTO workflow_runs (id, workflow_id, user_id, product_id, workflow_revision, definition_snapshot, status,
                                   idempotency_key, request_hash, created_at, updated_at, completed_at)
        VALUES (v_run, v_workflow, p_user, v_product, 1, '{}', 'completed', 'demo-' || v_run, repeat('0', 64),
                p_at, p_at + interval '40 minutes', p_at + interval '40 minutes');
        INSERT INTO export_packages (user_id, type, name, status, expires_at, workflow_run_id, created_at)
        VALUES (p_user, 'zip_package', 'Etsy upload package',
                CASE WHEN p_at + interval '30 days' < now() THEN 'expired' ELSE 'ready' END,
                p_at + interval '30 days', v_run, p_at + interval '40 minutes');
    END IF;
END $$;

-- A moment in [p_from, p_to]. Customers about to leave make most of their things early on.
CREATE FUNCTION pg_temp.demo_moment(p_from timestamptz, p_to timestamptz, p_fading boolean)
RETURNS timestamptz LANGUAGE sql AS $$
    SELECT date_trunc('hour', p_from + (CASE WHEN p_fading THEN 1 - sqrt(random()) ELSE random() END) * (p_to - p_from))
           + floor(random() * 60) * interval '1 minute'
$$;

DO $$
DECLARE
    v_now      timestamptz := now();
    v_sub      record;
    v_user     record;
    v_batch    uuid;
    v_from     timestamptz;
    v_to       timestamptz;
    v_fading   boolean;
    v_mode     text;
    v_products int;
    v_pro      boolean;
    v_trial_mockups int;
    v_cap      boolean;
    v_month    date;
    v_at       timestamptz;
    v_hit      boolean;
    v_day      date;
    v_design   uuid;
    v_product  uuid;
    v_template uuid;
    -- Per customer, whether they use each optional feature at all.
    v_uses_lifestyle boolean;
    v_uses_keyframe  boolean;
    v_uses_ai_video  boolean;
    v_uses_video     boolean;
    v_uses_landscape boolean;
    v_uses_auto      boolean;
    v_uses_regen     boolean;
    v_uses_export    boolean;
BEGIN
    PERFORM setseed(0.2727);

    -- Paying customers: from their first payment to the day they stopped paying, or today.
    FOR v_sub IN
        SELECT s.*, p.tier
        FROM subscriptions s
        JOIN subscription_plans p ON p.id = s.plan_id
        JOIN users u ON u.id = s.user_id
        WHERE u.email LIKE '%@apcs-demo.local' AND s.trial_ends_at IS NULL
          AND EXISTS (SELECT 1 FROM invoices i WHERE i.subscription_id = s.id AND i.status IN ('paid', 'refunded'))
        ORDER BY s.created_at
    LOOP
        -- How a customer who stops paying used the plan: one of Plan Insights' leave groups.
        v_mode := CASE WHEN v_sub.status = 'active' AND v_sub.auto_renew THEN CASE WHEN random() < 0.10 THEN 'never' ELSE 'stay' END
                       ELSE (ARRAY['never','never','never','failures','failures','limits','limits','faded','faded','faded',
                                   'steady','steady'])[1 + floor(random() * 12)::int] END;
        IF v_mode = 'limits' AND v_sub.tier <> 'starter' THEN v_mode := 'faded'; END IF;
        CONTINUE WHEN v_mode = 'never';
        v_fading := v_mode = 'faded';

        v_from := v_sub.created_at + interval '30 minutes';
        v_to := least(v_now - interval '1 hour', coalesce(v_sub.cancelled_at, v_sub.renewal_date::timestamptz));
        CONTINUE WHEN v_to <= v_from;
        -- Fading out needs a month of use to fall from, as Plan Insights compares the last 30 days with the 30 before.
        IF v_mode = 'faded' AND v_to - v_from < interval '40 days' THEN v_mode := 'steady'; v_fading := false; END IF;
        v_pro := v_sub.tier = 'pro';

        -- Products a month: Starter 6-16, Pro 10-30.
        v_products := greatest(1, round(extract(epoch FROM v_to - v_from) / 2592000.0
                      * CASE WHEN v_pro THEN 10 + random() * 20 ELSE 6 + random() * 10 END));

        v_uses_lifestyle := v_pro AND random() < 0.65;
        v_uses_keyframe := v_pro AND random() < 0.2;
        v_uses_ai_video := v_pro AND random() < 0.22;
        v_uses_video := random() < CASE WHEN v_pro THEN 0.55 ELSE 0.45 END;
        v_uses_landscape := v_pro AND random() < 0.3;
        v_uses_auto := random() < CASE WHEN v_pro THEN 0.5 ELSE 0.3 END;
        v_uses_regen := v_pro AND random() < 0.45;
        v_uses_export := random() < CASE WHEN v_pro THEN 0.5 ELSE 0.4 END;

        v_batch := gen_random_uuid();
        INSERT INTO batches (id, user_id, name, input_method, status, created_at)
        VALUES (v_batch, v_sub.user_id, 'Shop catalog', 'manual', 'completed', v_from);

        FOR i IN 1..v_products LOOP
            PERFORM pg_temp.demo_product(
                v_sub.user_id, v_batch,
                CASE WHEN NOT v_fading THEN pg_temp.demo_moment(v_from, v_to, false)
                     -- Most of it in the month before the last one, little in the last 30 days.
                     WHEN random() < 0.85 THEN pg_temp.demo_moment(greatest(v_from, v_to - interval '60 days'), v_to - interval '30 days', false)
                     ELSE pg_temp.demo_moment(v_to - interval '30 days', v_to, false) END,
                CASE WHEN v_pro THEN 1 + floor(random() * 4)::int ELSE 1 + floor(random() * 2)::int END,
                2 + floor(random() * CASE WHEN v_pro THEN 5 ELSE 3 END)::int,
                v_uses_auto AND random() < 0.8,
                v_uses_lifestyle AND random() < 0.5, v_uses_keyframe AND random() < 0.6, random() < 0.1,
                v_pro AND random() < 0.85, v_uses_regen AND random() < 0.3,
                CASE WHEN v_uses_video AND random() < 0.5 THEN CASE WHEN v_uses_landscape AND random() < 0.4 THEN 'landscape' ELSE 'portrait' END END,
                v_uses_ai_video AND random() < 0.4, v_uses_export AND random() < 0.3,
                CASE WHEN v_mode = 'failures' THEN 0.45 ELSE 0.05 END);
        END LOOP;

        -- Starter customers who left after reaching the 300 mockups a day cap on 3 of their last 30 days.
        IF v_mode = 'limits' THEN
            FOR d IN 1..3 LOOP
                v_at := date_trunc('day', v_to - (d * 6 + floor(random() * 3)::int) * interval '1 day') + interval '9 hours';
                CONTINUE WHEN v_at <= v_from;
                PERFORM pg_temp.demo_product(v_sub.user_id, v_batch, v_at, 2, 0, true,
                                             false, false, false, false, false, NULL, false, false);
                SELECT p.id, di.id INTO v_product, v_design
                FROM products p JOIN design_images di ON di.product_id = p.id
                WHERE p.user_id = v_sub.user_id AND p.created_at = v_at LIMIT 1;
                SELECT id INTO v_template FROM mockup_templates WHERE user_id IS NULL ORDER BY created_at LIMIT 1;
                INSERT INTO mockup_images (product_id, design_image_id, mockup_template_id, storage_key, mockup_width_px,
                                           mockup_height_px, source_type, approval_status, created_at)
                SELECT v_product, v_design, v_template, 'demo/burst', 1600, 1600, 'generated', 'approved', v_at + n * interval '90 seconds'
                FROM generate_series(1, 300) AS n;
            END LOOP;
        END IF;

        -- Pro customers who brought their own mockup photos. Template names are unique across the database.
        IF v_pro AND random() < 0.35 THEN
            INSERT INTO mockup_templates (user_id, name, product_type, base_image_url, output_width_px, output_height_px, created_at)
            VALUES (v_sub.user_id, 'Studio photo ' || upper(substr(md5(random()::text), 1, 6)), 'tshirt', 'https://example.invalid/template.png', 1600, 1600,
                    pg_temp.demo_moment(v_from, v_to, false));
        END IF;
    END LOOP;

    -- Pro trials: everything in Pro except AI video and studio keyframes, inside the trial allowances.
    FOR v_sub IN
        SELECT s.* FROM subscriptions s JOIN users u ON u.id = s.user_id
        WHERE u.email LIKE '%@apcs-demo.local' AND s.trial_ends_at IS NOT NULL
        ORDER BY s.created_at
    LOOP
        CONTINUE WHEN random() < 0.12;
        v_from := v_sub.created_at + interval '20 minutes';
        v_to := least(v_now - interval '1 hour', v_sub.trial_ends_at, coalesce(v_sub.cancelled_at, v_sub.trial_ends_at));
        CONTINUE WHEN v_to <= v_from;
        -- Trials that end up buying explore more.
        v_products := least(10, CASE WHEN v_sub.status = 'cancelled' THEN 5 + floor(random() * 6) ELSE 2 + floor(random() * 6) END::int);
        v_cap := random() < 0.2;                       -- uses the whole mockup allowance
        v_trial_mockups := 0;
        v_uses_lifestyle := random() < CASE WHEN v_sub.status = 'cancelled' THEN 0.7 ELSE 0.35 END;
        v_uses_landscape := random() < 0.15;
        v_uses_auto := random() < 0.3;
        v_uses_regen := random() < 0.25;

        FOR i IN 1..v_products LOOP
            IF (i - 1) % 3 = 0 THEN                    -- up to 3 products a run
                v_batch := gen_random_uuid();
                INSERT INTO batches (id, user_id, name, input_method, status, created_at)
                VALUES (v_batch, v_sub.user_id, 'Trial run ' || ((i - 1) / 3 + 1), 'manual', 'completed', v_from);
            END IF;
            v_at := pg_temp.demo_moment(v_from, v_to, false);
            PERFORM pg_temp.demo_product(
                v_sub.user_id, v_batch, v_at,
                CASE WHEN random() < 0.6 THEN 2 + floor(random() * 3)::int ELSE 1 END,
                CASE WHEN v_cap THEN ceil((100 - v_trial_mockups)::numeric / (v_products - i + 1))::int ELSE 2 + floor(random() * 4)::int END,
                v_uses_auto,
                v_uses_lifestyle AND random() < 0.5, false, random() < 0.1,
                random() < 0.7 AND i <= 18, v_uses_regen AND random() < 0.5,
                CASE WHEN i <= 10 AND random() < 0.5 THEN CASE WHEN v_uses_landscape THEN 'landscape' ELSE 'portrait' END END,
                false, random() < 0.2);
            v_trial_mockups := v_trial_mockups + CASE WHEN v_cap THEN ceil((100 - v_trial_mockups)::numeric / (v_products - i + 1))::int ELSE 0 END;
        END LOOP;

        IF random() < 0.2 THEN
            INSERT INTO mockup_templates (user_id, name, product_type, base_image_url, output_width_px, output_height_px, created_at)
            VALUES (v_sub.user_id, 'Trial template ' || upper(substr(md5(random()::text), 1, 6)), 'tshirt', 'https://example.invalid/template.png', 1600, 1600,
                    pg_temp.demo_moment(v_from, v_to, false));
        END IF;
    END LOOP;

    -- Free customers, month by month: one product and one design a run, inside the monthly allowances.
    FOR v_user IN
        SELECT u.id, u.created_at,
               (SELECT max(coalesce(s.cancelled_at, s.trial_ends_at)) FROM subscriptions s
                WHERE s.user_id = u.id AND s.trial_ends_at IS NOT NULL) AS trial_ended
        FROM users u
        WHERE u.email LIKE '%@apcs-demo.local'
          AND NOT EXISTS (SELECT 1 FROM subscriptions s WHERE s.user_id = u.id AND s.trial_ends_at IS NULL AND s.status <> 'trialing')
          AND NOT EXISTS (SELECT 1 FROM subscriptions s WHERE s.user_id = u.id AND s.trial_ends_at > v_now AND s.status = 'active')
        ORDER BY u.created_at
    LOOP
        CONTINUE WHEN random() < 0.30;                 -- signed up and never came back
        v_hit := random() < 0.25;
        v_batch := gen_random_uuid();
        INSERT INTO batches (id, user_id, name, input_method, status, created_at)
        VALUES (v_batch, v_user.id, 'My first designs', 'manual', 'completed', v_user.created_at);
        v_month := date_trunc('month', greatest(v_user.created_at, coalesce(v_user.trial_ended, v_user.created_at)))::date;
        WHILE v_month <= v_now::date LOOP
            v_from := greatest(v_month::timestamptz, v_user.created_at, coalesce(v_user.trial_ended, v_user.created_at)) + interval '1 hour';
            v_to := least((v_month + interval '1 month')::timestamptz, v_now - interval '1 hour');
            IF v_to > v_from THEN
                -- A customer at the limit uses 6 of 6 mockups and 3 designs; the others 1-2 products.
                v_products := CASE WHEN v_hit AND v_month = date_trunc('month', v_now)::date THEN 3 ELSE 1 + floor(random() * 2)::int END;
                FOR i IN 1..v_products LOOP
                    PERFORM pg_temp.demo_product(
                        v_user.id, v_batch, pg_temp.demo_moment(v_from, v_to, false),
                        1, CASE WHEN v_hit AND v_month = date_trunc('month', v_now)::date THEN 2 ELSE 1 + floor(random() * 2)::int END,
                        false, false, false, false,
                        random() < 0.6, false,
                        CASE WHEN i = 1 AND random() < 0.4 THEN 'portrait' END,
                        false, false);
                END LOOP;
            END IF;
            v_month := (v_month + interval '1 month')::date;
        END LOOP;
    END LOOP;

    -- Three Starter customers who sell a lot: 300 mockups, the daily cap, on 3 days of their last 30,
    -- and as many designs as a typical Pro Seller, so Plan Insights offers them Pro.
    FOR v_user IN
        SELECT s.user_id AS id FROM subscriptions s
        JOIN subscription_plans p ON p.id = s.plan_id JOIN users u ON u.id = s.user_id
        WHERE u.email LIKE '%@apcs-demo.local' AND p.tier = 'starter' AND s.status = 'active' AND s.auto_renew
          AND s.trial_ends_at IS NULL AND s.start_date < current_date - 35
        ORDER BY s.start_date LIMIT 3
    LOOP
        SELECT id INTO v_batch FROM batches WHERE user_id = v_user.id ORDER BY created_at LIMIT 1;
        IF v_batch IS NULL THEN
            v_batch := gen_random_uuid();
            INSERT INTO batches (id, user_id, name, input_method, status, created_at)
            VALUES (v_batch, v_user.id, 'Shop catalog', 'manual', 'completed', v_now - interval '40 days');
        END IF;
        FOR d IN 1..3 LOOP
            v_day := current_date - (d * 7 + floor(random() * 4)::int);
            v_at := v_day + interval '9 hours';
            FOR k IN 1..12 LOOP
                PERFORM pg_temp.demo_product(v_user.id, v_batch, v_at + k * interval '10 minutes', 2, 0, true,
                                             false, false, false, false, false, CASE WHEN k <= 2 THEN 'portrait' END, false, k = 1);
            END LOOP;
            SELECT p.id, di.id INTO v_product, v_design
            FROM products p JOIN design_images di ON di.product_id = p.id
            WHERE p.user_id = v_user.id AND p.created_at = v_at + interval '10 minutes' LIMIT 1;
            SELECT id INTO v_template FROM mockup_templates WHERE user_id IS NULL ORDER BY created_at LIMIT 1;
            INSERT INTO mockup_images (product_id, design_image_id, mockup_template_id, storage_key, mockup_width_px, mockup_height_px,
                                       source_type, approval_status, created_at)
            SELECT v_product, v_design, v_template, 'demo/burst', 1600, 1600, 'generated', 'approved', v_at + n * interval '90 seconds'
            FROM generate_series(1, 300) AS n;
        END LOOP;
    END LOOP;
END $$;

COMMIT;

-- Summary printed by the runner script.
SELECT 'products' AS what, count(*) FROM products p JOIN users u ON u.id = p.user_id WHERE u.email LIKE '%@apcs-demo.local'
UNION ALL SELECT 'design images', count(*) FROM design_images d JOIN products p ON p.id = d.product_id JOIN users u ON u.id = p.user_id WHERE u.email LIKE '%@apcs-demo.local'
UNION ALL SELECT 'mockups', count(*) FROM mockup_images m JOIN products p ON p.id = m.product_id JOIN users u ON u.id = p.user_id WHERE u.email LIKE '%@apcs-demo.local'
UNION ALL SELECT 'videos', count(*) FROM promo_videos v JOIN products p ON p.id = v.product_id JOIN users u ON u.id = p.user_id WHERE u.email LIKE '%@apcs-demo.local'
UNION ALL SELECT 'api calls', count(*) FROM api_usage_records r JOIN users u ON u.id = r.user_id WHERE u.email LIKE '%@apcs-demo.local';
