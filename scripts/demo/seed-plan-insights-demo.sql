-- Seeds usage and free trials for the demo customers so the Admin's Plan Insights tab has
-- something to show: designs, mockups, lifestyle mockups, keyframes, listings, videos, exports,
-- a Seller at the daily mockup cap, and trials that converted, ended or are still running.
--
-- DEMO DATABASE ONLY. It writes rows for the @apcs-demo.local customers and removes what an
-- earlier run added first, so it is safe to run again. Run it after subscription-entitlements.sql.

BEGIN;

DO $$
DECLARE
    v_now        timestamptz := now();
    v_customer   record;
    v_batch      uuid;
    v_workflow   uuid;
    v_product    uuid;
    v_prompt     uuid;
    v_design     uuid;
    v_usage      uuid;
    v_run        uuid;
    v_template   uuid;
    v_video_tpl  uuid;
    v_pro_plan   uuid;
    v_starter    uuid;
    v_is_pro     boolean;
    v_products   int;
    v_designs    int;
    v_created    timestamptz;
    v_has_mockup boolean;
    v_has_video  boolean;
    v_trial      uuid;
    v_trial_start timestamptz;
    v_trial_end  timestamptz;
    v_converted  boolean;
    v_paid_plan  uuid;
    v_paid_sub   uuid;
    v_price      numeric;
    v_cap_user   uuid;
    v_cap_design uuid;
    v_cap_day    timestamptz;
    -- PL/pgSQL ends an IF condition at its first THEN, so a CASE cannot sit inside one.
    v_lifestyle_odds numeric;
    v_ai_video_odds  numeric;
    i int;
    j int;
BEGIN
    PERFORM setseed(0.4343);

    -- Remove what an earlier run added, children first.
    DELETE FROM export_packages WHERE name = 'Insights demo export';
    DELETE FROM workflow_runs WHERE idempotency_key LIKE 'insights-demo-%';
    DELETE FROM workflows WHERE name = 'Insights demo workflow';
    DELETE FROM promo_videos WHERE product_id IN (
        SELECT p.id FROM products p JOIN batches b ON b.id = p.batch_id WHERE b.name = 'Insights demo batch');
    DELETE FROM mockup_images WHERE product_id IN (
        SELECT p.id FROM products p JOIN batches b ON b.id = p.batch_id WHERE b.name = 'Insights demo batch');
    DELETE FROM design_images WHERE product_id IN (
        SELECT p.id FROM products p JOIN batches b ON b.id = p.batch_id WHERE b.name = 'Insights demo batch');
    DELETE FROM ai_prompts WHERE product_id IN (
        SELECT p.id FROM products p JOIN batches b ON b.id = p.batch_id WHERE b.name = 'Insights demo batch');
    DELETE FROM api_usage_records WHERE provider_request_id = 'insights-demo';
    DELETE FROM products WHERE batch_id IN (SELECT id FROM batches WHERE name = 'Insights demo batch');
    DELETE FROM batches WHERE name = 'Insights demo batch';

    CREATE TEMP TABLE seeded_trial_subscriptions ON COMMIT DROP AS
        SELECT subscription_id AS id FROM invoices WHERE invoice_number LIKE 'INV-DEMOTRIAL-%'
        UNION
        SELECT s.id FROM subscriptions s JOIN users u ON u.id = s.user_id
        WHERE s.trial_ends_at IS NOT NULL AND u.email LIKE '%@apcs-demo.local';
    DELETE FROM invoices WHERE invoice_number LIKE 'INV-DEMOTRIAL-%';
    DELETE FROM subscriptions WHERE id IN (SELECT id FROM seeded_trial_subscriptions);

    SELECT id INTO v_template FROM mockup_templates WHERE user_id IS NULL ORDER BY created_at LIMIT 1;
    SELECT id INTO v_video_tpl FROM video_templates ORDER BY created_at LIMIT 1;
    SELECT id INTO v_pro_plan FROM subscription_plans WHERE tier = 'pro';
    SELECT id INTO v_starter FROM subscription_plans WHERE tier = 'starter';
    IF v_template IS NULL OR v_video_tpl IS NULL OR v_pro_plan IS NULL OR v_starter IS NULL THEN
        RAISE EXCEPTION 'Needs a system mockup template, a video template and the starter and pro plans.';
    END IF;

    -- Usage for every demo customer on a paid plan; about one in seven used nothing.
    FOR v_customer IN
        SELECT s.user_id, p.tier
        FROM subscriptions s
        JOIN subscription_plans p ON p.id = s.plan_id
        JOIN users u ON u.id = s.user_id
        WHERE u.email LIKE '%@apcs-demo.local' AND s.status = 'active' AND s.trial_ends_at IS NULL
        ORDER BY s.user_id
    LOOP
        CONTINUE WHEN random() < 0.15;
        v_is_pro := v_customer.tier IN ('pro', 'studio');

        v_batch := gen_random_uuid();
        INSERT INTO batches (id, user_id, name, input_method, status, created_at)
        VALUES (v_batch, v_customer.user_id, 'Insights demo batch', 'manual', 'completed', v_now - interval '89 days');
        v_workflow := gen_random_uuid();
        INSERT INTO workflows (id, user_id, name, definition)
        VALUES (v_workflow, v_customer.user_id, 'Insights demo workflow', '{"version":2,"nodes":[],"edges":[]}');

        v_products := 2 + floor(random() * CASE WHEN v_is_pro THEN 14 ELSE 7 END)::int;
        FOR i IN 1..v_products LOOP
            v_created := v_now - (random() * 85) * interval '1 day';
            v_product := gen_random_uuid();
            INSERT INTO products (id, user_id, name, product_type, input_description, batch_id, processing_status, created_at)
            VALUES (v_product, v_customer.user_id, 'Demo product ' || i, 'tshirt', 'Seeded for Plan Insights', v_batch, 'pending', v_created);

            v_has_mockup := false;
            IF random() < 0.9 THEN
                v_prompt := gen_random_uuid();
                INSERT INTO ai_prompts (id, product_id, original_description, system_prompt, generated_prompt, created_at)
                VALUES (v_prompt, v_product, 'Seeded', 'Seeded', 'Seeded prompt', v_created);

                v_designs := 1 + floor(random() * CASE WHEN v_is_pro THEN 4 ELSE 2 END)::int;
                FOR j IN 1..v_designs LOOP
                    v_usage := gen_random_uuid();
                    INSERT INTO api_usage_records (id, user_id, product_id, provider, feature, request_units, cost_usd, status, provider_request_id, created_at)
                    VALUES (v_usage, v_customer.user_id, v_product, 'gemini', 'image_generation', 1, 0.039,
                            CASE WHEN random() < 0.05 THEN 'failed' ELSE 'success' END, 'insights-demo', v_created);
                    v_design := gen_random_uuid();
                    INSERT INTO design_images (id, product_id, ai_prompt_id, api_usage_record_id, image_generator_model, storage_key, image_url,
                                               image_width_px, image_height_px, file_format, file_size_mb, generation_time_seconds,
                                               approval_status, variation_index, created_at)
                    VALUES (v_design, v_product, v_prompt, v_usage, 'gemini-3.1-flash-image', 'demo/' || v_design, 'https://example.invalid/' || v_design || '.png',
                            1024, 1024, 'png', 1.2, 8.5,
                            CASE WHEN random() < 0.75 THEN 'approved' ELSE 'pending' END, j, v_created + interval '5 minutes');

                    IF random() < 0.7 THEN
                        INSERT INTO mockup_images (id, product_id, design_image_id, mockup_template_id, storage_key, mockup_width_px, mockup_height_px,
                                                   source_type, approval_status, created_at)
                        SELECT gen_random_uuid(), v_product, v_design, v_template, 'demo/mockup', 1600, 1600, 'generated', 'approved',
                               v_created + interval '10 minutes'
                        FROM generate_series(1, 2 + floor(random() * 2)::int);
                        v_has_mockup := true;
                    END IF;
                END LOOP;
            END IF;

            IF random() < 0.1 THEN
                INSERT INTO mockup_images (id, product_id, storage_key, mockup_width_px, mockup_height_px, source_type, approval_status, created_at)
                VALUES (gen_random_uuid(), v_product, 'demo/upload', 1600, 1600, 'uploaded', 'approved', v_created + interval '12 minutes');
                v_has_mockup := true;
            END IF;

            -- AI tools are Pro: lifestyle mockups and studio keyframes on the Seller's own key.
            IF v_is_pro AND random() < 0.25 THEN
                v_usage := gen_random_uuid();
                INSERT INTO api_usage_records (id, user_id, product_id, provider, feature, request_units, cost_usd, status, provider_request_id, created_at)
                VALUES (v_usage, v_customer.user_id, v_product, 'gemini', 'mockup', 1, 0.039, 'success', 'insights-demo', v_created + interval '15 minutes');
                INSERT INTO mockup_images (id, product_id, api_usage_record_id, storage_key, mockup_width_px, mockup_height_px, source_type, role, approval_status, created_at)
                VALUES (gen_random_uuid(), v_product, v_usage, 'demo/lifestyle', 1024, 1536, 'uploaded', 'Lifestyle', 'pending', v_created + interval '15 minutes');
            END IF;
            IF v_is_pro AND random() < 0.12 THEN
                v_usage := gen_random_uuid();
                INSERT INTO api_usage_records (id, user_id, product_id, provider, feature, request_units, cost_usd, status, provider_request_id, created_at)
                VALUES (v_usage, v_customer.user_id, v_product, 'gemini', 'image_generation', 1, 0.039, 'success', 'insights-demo', v_created + interval '16 minutes');
                INSERT INTO mockup_images (id, product_id, api_usage_record_id, storage_key, mockup_width_px, mockup_height_px, source_type, approval_status, created_at)
                VALUES (gen_random_uuid(), v_product, v_usage, 'demo/keyframe', 1080, 1920, 'ai_generated', 'approved', v_created + interval '16 minutes');
            END IF;

            -- Listings run on the platform key, with a few failures.
            IF random() < 0.8 THEN
                INSERT INTO api_usage_records (id, user_id, product_id, provider, feature, request_units, cost_usd, status, provider_request_id, created_at)
                VALUES (gen_random_uuid(), v_customer.user_id, v_product, 'gemini', 'listing', 1, 0.0004,
                        CASE WHEN random() < 0.06 THEN 'failed' ELSE 'success' END, 'insights-demo', v_created + interval '20 minutes');
            END IF;

            v_has_video := false;
            IF v_has_mockup AND random() < 0.55 THEN
                INSERT INTO promo_videos (id, product_id, video_template_id, video_duration_seconds, video_resolution, file_format, aspect_ratio,
                                          platform_target, status, approval_status, mode, created_at)
                VALUES (gen_random_uuid(), v_product, v_video_tpl, 12, '1080x1920', 'mp4', '9:16', 'etsy', 'completed', 'approved', 'standard',
                        v_created + interval '30 minutes');
                v_has_video := true;
            END IF;
            IF v_is_pro AND v_has_mockup AND random() < 0.2 THEN
                INSERT INTO promo_videos (id, product_id, video_template_id, video_duration_seconds, video_resolution, file_format, aspect_ratio,
                                          platform_target, status, approval_status, mode, created_at)
                VALUES (gen_random_uuid(), v_product, v_video_tpl, 16, '720x1280', 'mp4', '9:16', 'etsy', 'completed', 'approved', 'ai_shot',
                        v_created + interval '35 minutes');
                v_has_video := true;
            END IF;

            IF v_has_video AND random() < 0.6 THEN
                v_run := gen_random_uuid();
                INSERT INTO workflow_runs (id, workflow_id, user_id, product_id, workflow_revision, definition_snapshot, status,
                                           idempotency_key, request_hash, created_at, updated_at, completed_at)
                VALUES (v_run, v_workflow, v_customer.user_id, v_product, 1, '{}', 'completed',
                        'insights-demo-' || v_run, repeat('0', 64), v_created, v_created + interval '40 minutes', v_created + interval '40 minutes');
                INSERT INTO export_packages (id, user_id, type, name, status, expires_at, workflow_run_id, created_at)
                VALUES (gen_random_uuid(), v_customer.user_id, 'zip_package', 'Insights demo export', 'ready', v_now + interval '30 days', v_run,
                        v_created + interval '40 minutes');
            END IF;
        END LOOP;
    END LOOP;

    -- One Starter Seller who rendered 310 mockups in a single day, past the 300 cap.
    SELECT s.user_id INTO v_cap_user
    FROM subscriptions s JOIN subscription_plans p ON p.id = s.plan_id JOIN users u ON u.id = s.user_id
    WHERE p.tier = 'starter' AND s.status = 'active' AND s.trial_ends_at IS NULL AND u.email LIKE '%@apcs-demo.local'
    ORDER BY s.user_id LIMIT 1;
    SELECT d.id, d.created_at INTO v_cap_design, v_cap_day
    FROM design_images d JOIN products p ON p.id = d.product_id
    WHERE p.user_id = v_cap_user ORDER BY d.created_at DESC LIMIT 1;
    IF v_cap_design IS NOT NULL THEN
        INSERT INTO mockup_images (id, product_id, design_image_id, mockup_template_id, storage_key, mockup_width_px, mockup_height_px,
                                   source_type, approval_status, created_at)
        SELECT gen_random_uuid(), d.product_id, d.id, v_template, 'demo/burst', 1600, 1600, 'generated', 'pending',
               date_trunc('day', v_cap_day) + interval '9 hours' + n * interval '1 minute'
        FROM design_images d, generate_series(1, 310) AS n
        WHERE d.id = v_cap_design;
    END IF;

    -- Free Pro trials for customers with no plan now: some bought, some ended, some still running.
    FOR v_customer IN
        SELECT u.id AS user_id
        FROM users u
        WHERE u.email LIKE '%@apcs-demo.local'
          AND NOT EXISTS (SELECT 1 FROM subscriptions s WHERE s.user_id = u.id AND s.status = 'active')
        ORDER BY u.id
        LIMIT 70
    LOOP
        v_trial_start := v_now - (random() * 58) * interval '1 day';
        v_trial_end := v_trial_start + interval '14 days';
        v_converted := v_trial_end < v_now AND random() < 0.35;
        v_lifestyle_odds := CASE WHEN v_converted THEN 0.6 ELSE 0.15 END;
        v_ai_video_odds := CASE WHEN v_converted THEN 0.5 ELSE 0.1 END;
        v_trial := gen_random_uuid();

        INSERT INTO subscriptions (id, user_id, plan_id, billing_cycle, monthly_price_usd, status, start_date, renewal_date,
                                   trial_ends_at, auto_renew, created_at, updated_at)
        VALUES (v_trial, v_customer.user_id, v_pro_plan, 'monthly', 0,
                CASE WHEN v_converted THEN 'cancelled' WHEN v_trial_end < v_now THEN 'expired' ELSE 'active' END,
                v_trial_start::date, v_trial_end::date, v_trial_end, false, v_trial_start, v_trial_start);

        -- What the trial user tried; Sellers who later buy tend to use the AI tools.
        v_batch := gen_random_uuid();
        INSERT INTO batches (id, user_id, name, input_method, status, created_at)
        VALUES (v_batch, v_customer.user_id, 'Insights demo batch', 'manual', 'completed', v_trial_start);
        FOR i IN 1..(1 + floor(random() * 4)::int) LOOP
            v_created := v_trial_start + (random() * 13) * interval '1 day';
            CONTINUE WHEN v_created > v_now;
            v_product := gen_random_uuid();
            INSERT INTO products (id, user_id, name, product_type, input_description, batch_id, processing_status, created_at)
            VALUES (v_product, v_customer.user_id, 'Trial product ' || i, 'tshirt', 'Seeded for Plan Insights', v_batch, 'pending', v_created);
            v_prompt := gen_random_uuid();
            INSERT INTO ai_prompts (id, product_id, original_description, system_prompt, generated_prompt, created_at)
            VALUES (v_prompt, v_product, 'Seeded', 'Seeded', 'Seeded prompt', v_created);
            v_design := gen_random_uuid();
            INSERT INTO design_images (id, product_id, ai_prompt_id, image_generator_model, storage_key, image_url, image_width_px, image_height_px,
                                       file_format, file_size_mb, generation_time_seconds, approval_status, variation_index, created_at)
            VALUES (v_design, v_product, v_prompt, 'gemini-3.1-flash-image', 'demo/' || v_design, 'https://example.invalid/' || v_design || '.png',
                    1024, 1024, 'png', 1.2, 8.5, 'approved', 1, v_created);
            INSERT INTO mockup_images (id, product_id, design_image_id, mockup_template_id, storage_key, mockup_width_px, mockup_height_px,
                                       source_type, approval_status, created_at)
            VALUES (gen_random_uuid(), v_product, v_design, v_template, 'demo/mockup', 1600, 1600, 'generated', 'approved', v_created);
            IF random() < v_lifestyle_odds THEN
                v_usage := gen_random_uuid();
                INSERT INTO api_usage_records (id, user_id, product_id, provider, feature, request_units, cost_usd, status, provider_request_id, created_at)
                VALUES (v_usage, v_customer.user_id, v_product, 'gemini', 'mockup', 1, 0.039, 'success', 'insights-demo', v_created);
                INSERT INTO mockup_images (id, product_id, api_usage_record_id, storage_key, mockup_width_px, mockup_height_px, source_type, role, approval_status, created_at)
                VALUES (gen_random_uuid(), v_product, v_usage, 'demo/lifestyle', 1024, 1536, 'uploaded', 'Lifestyle', 'pending', v_created);
            END IF;
            IF random() < v_ai_video_odds THEN
                INSERT INTO promo_videos (id, product_id, video_template_id, video_duration_seconds, video_resolution, file_format, aspect_ratio,
                                          platform_target, status, approval_status, mode, created_at)
                VALUES (gen_random_uuid(), v_product, v_video_tpl, 16, '720x1280', 'mp4', '9:16', 'etsy', 'completed', 'approved', 'ai_shot', v_created);
            END IF;
            IF random() < 0.7 THEN
                INSERT INTO api_usage_records (id, user_id, product_id, provider, feature, request_units, cost_usd, status, provider_request_id, created_at)
                VALUES (gen_random_uuid(), v_customer.user_id, v_product, 'gemini', 'listing', 1, 0.0004, 'success', 'insights-demo', v_created);
            END IF;
        END LOOP;

        IF v_converted THEN
            v_paid_plan := CASE WHEN random() < 0.7 THEN v_pro_plan ELSE v_starter END;
            SELECT monthly_price_usd INTO v_price FROM subscription_plans WHERE id = v_paid_plan;
            v_paid_sub := gen_random_uuid();
            INSERT INTO subscriptions (id, user_id, plan_id, billing_cycle, monthly_price_usd, status, start_date, renewal_date,
                                       auto_renew, created_at, updated_at)
            VALUES (v_paid_sub, v_customer.user_id, v_paid_plan, 'monthly', v_price, 'active',
                    (v_trial_end - interval '2 days')::date, ((v_trial_end - interval '2 days') + interval '1 month')::date,
                    true, v_trial_end - interval '2 days', v_trial_end - interval '2 days');
            INSERT INTO invoices (id, subscription_id, user_id, invoice_number, invoice_date, due_date, amount_usd, tax_amount, total_amount,
                                  status, payment_date, items, created_at)
            VALUES (gen_random_uuid(), v_paid_sub, v_customer.user_id, 'INV-DEMOTRIAL-' || substr(md5(v_paid_sub::text), 1, 8),
                    (v_trial_end - interval '2 days')::date, (v_trial_end - interval '2 days')::date, v_price, 0, v_price,
                    'paid', (v_trial_end - interval '2 days')::date, '[]', v_trial_end - interval '2 days');
        END IF;
    END LOOP;
END $$;

COMMIT;
