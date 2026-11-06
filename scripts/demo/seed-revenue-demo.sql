-- Revenue demo data: the first 14 weeks of APCS after launch, so the Revenue page, the Financial
-- Alerts card and the Plan Insights tab show a young but believable business.
--
-- ONLY for the throwaway demo database that scripts/demo/revenue-demo.sh starts in Docker. Every
-- demo customer uses the @apcs-demo.local domain, which is how a reseed finds and removes them.
-- Run it after subscription-entitlements.sql and plan-access-update.sql, then run
-- seed-plan-insights-demo.sql for what the customers made.
--
-- Why 14 weeks: the revenue forecast needs 8 whole weeks of history to draw and 14 to drop its
-- "low confidence" label, and Plan Insights looks at the last 45 days plus 60 days before them.
--
-- The story the numbers tell:
--   * Sign-ups grow from about 35 to about 90 a week (about 900 in all), so each plan has enough
--     customers who stayed and who left for Plan Insights to compare them (10 of each).
--   * Only Starter and Pro are sold; Studio is no longer offered, so no demo customer holds it.
--   * Weeks 1-4: half stay on Free, 25% start the trial, 12% buy Starter, 13% buy Pro.
--   * From week 5: half stay on Free, 30% start the 14-day Pro trial, 14% buy Starter, 6% buy Pro.
--   * A trial converts when the Seller pays while it runs: 30% buy Pro, 10% Starter, the rest
--     go back to Free.
--   * 10% of paying customers pay yearly. Monthly churn: Starter 12%, Pro 5%.
--   * About 3% of renewals fail and 1% are refunded; 2% abandon the checkout.
-- The rows follow the rules the backend jobs enforce (SubscriptionRenewalJob, TrialExpiryJob), so
-- starting the backend does not rewrite them: a running paid subscription renews in the future, an
-- unpaid one is past_due for its 3 grace days and expired after, a running trial ends in the future.
-- setseed() makes every run produce the same rows.

\set ON_ERROR_STOP on

BEGIN;

-- Remove an earlier run: every row the demo customers own, children first.
CREATE TEMP TABLE demo_users ON COMMIT DROP AS
    SELECT id FROM users WHERE email LIKE '%@apcs-demo.local';

DELETE FROM listing_generation_history WHERE listing_content_id IN (
    SELECT lc.id FROM listing_contents lc JOIN products p ON p.id = lc.product_id WHERE p.user_id IN (SELECT id FROM demo_users));
DELETE FROM listing_contents WHERE product_id IN (SELECT id FROM products WHERE user_id IN (SELECT id FROM demo_users));
DELETE FROM export_packages WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM workflow_runs WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM workflows WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM promo_videos WHERE product_id IN (SELECT id FROM products WHERE user_id IN (SELECT id FROM demo_users));
DELETE FROM mockup_images WHERE product_id IN (SELECT id FROM products WHERE user_id IN (SELECT id FROM demo_users));
DELETE FROM design_images WHERE product_id IN (SELECT id FROM products WHERE user_id IN (SELECT id FROM demo_users));
DELETE FROM ai_prompts WHERE product_id IN (SELECT id FROM products WHERE user_id IN (SELECT id FROM demo_users));
DELETE FROM api_usage_records WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM batch_jobs WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM products WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM batches WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM mockup_templates WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM invoices WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM subscriptions WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM auth_tokens WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM user_roles WHERE user_id IN (SELECT id FROM demo_users);
DELETE FROM users WHERE id IN (SELECT id FROM demo_users);

CREATE TEMP SEQUENCE demo_order_code START 1000001;
-- Every tenth purchase is yearly, so the yearly payments spread over the weeks instead of bunching.
CREATE TEMP SEQUENCE demo_purchase_no;

-- A paid subscription from its first payment until it churns, fails a renewal or reaches today.
CREATE FUNCTION pg_temp.demo_paid_subscription(p_user uuid, p_tier text, p_at timestamptz, p_annual boolean)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    v_plan      record;
    v_sub       uuid;
    v_today     date := current_date;
    v_price     numeric(10, 2);
    v_churn     numeric;
    v_bill      date := p_at::date;
    v_next      date;
    v_status    text;
    v_end_state text;
    v_end_at    timestamptz;
    v_renewal   date;
    v_auto      boolean := true;
    v_paid      int := 0;
    v_label     text;
    v_time      interval := p_at - p_at::date;
BEGIN
    SELECT * INTO v_plan FROM subscription_plans WHERE tier = p_tier;
    v_price := CASE WHEN p_annual THEN v_plan.annual_price_usd ELSE v_plan.monthly_price_usd END;
    v_churn := CASE p_tier WHEN 'starter' THEN 0.12 ELSE 0.05 END;
    v_label := v_plan.name || CASE WHEN p_annual THEN ' plan (annual)' ELSE ' plan (monthly)' END;

    INSERT INTO subscriptions (user_id, plan_id, billing_cycle, monthly_price_usd, annual_price_usd, status,
                               start_date, renewal_date, auto_renew, created_at, updated_at)
    VALUES (p_user, v_plan.id, CASE WHEN p_annual THEN 'annual' ELSE 'monthly' END, v_plan.monthly_price_usd,
            v_plan.annual_price_usd, 'active', v_bill, v_bill, true, p_at, p_at)
    RETURNING id INTO v_sub;

    -- 2% leave the checkout: the payment link expires unpaid.
    IF random() < 0.02 THEN
        INSERT INTO invoices (subscription_id, user_id, invoice_number, invoice_date, due_date, amount_usd, tax_amount,
                              total_amount, status, items, payos_order_code, created_at, updated_at)
        VALUES (v_sub, p_user, 'INV-' || to_char(v_bill, 'YYYYMMDD') || '-' || upper(substr(md5(random()::text), 1, 8)),
                v_bill, v_bill, v_price, 0, v_price, 'void',
                jsonb_build_array(jsonb_build_object('description', v_label, 'amount', v_price)),
                nextval('demo_order_code'), p_at, p_at);
        UPDATE subscriptions SET status = 'expired', auto_renew = false, cancelled_at = p_at + interval '15 minutes',
                                 renewal_date = v_bill, updated_at = p_at + interval '15 minutes'
        WHERE id = v_sub;
        RETURN;
    END IF;

    LOOP
        v_next := (v_bill + CASE WHEN p_annual THEN interval '1 year' ELSE interval '1 month' END)::date;
        v_status := CASE WHEN v_paid = 0 THEN 'paid'
                         WHEN random() < 0.03 THEN 'overdue'
                         WHEN random() < 0.01 THEN 'refunded'
                         ELSE 'paid' END;

        INSERT INTO invoices (subscription_id, user_id, invoice_number, invoice_date, due_date, amount_usd, tax_amount,
                              total_amount, status, payment_date, items, payos_order_code, stripe_invoice_id,
                              created_at, updated_at)
        VALUES (v_sub, p_user, 'INV-' || to_char(v_bill, 'YYYYMMDD') || '-' || upper(substr(md5(random()::text), 1, 8)),
                v_bill, v_bill + 3, v_price, 0, v_price, v_status,
                CASE WHEN v_status IN ('paid', 'refunded') THEN v_bill END,
                jsonb_build_array(jsonb_build_object('description', v_label, 'amount', v_price)),
                CASE WHEN random() < 0.85 THEN nextval('demo_order_code') END,
                NULL, v_bill + v_time, v_bill + v_time);
        v_paid := v_paid + 1;

        IF v_status = 'overdue' THEN
            -- The renewal payment failed: unpaid from this bill date on, past due for the grace days.
            v_renewal := v_bill;
            IF v_bill >= v_today - 2 THEN
                v_end_state := 'past_due';
                v_end_at := NULL;
            ELSE
                v_end_state := 'expired';
                v_end_at := v_bill::timestamptz;
            END IF;
            EXIT;
        ELSIF v_status = 'refunded' THEN
            -- Refunded a few days after paying; the plan ends there.
            v_renewal := v_bill;
            v_end_state := 'expired';
            v_end_at := v_bill + (1 + floor(random() * 4))::int + interval '10 hours';
            IF v_end_at > now() THEN v_end_at := now() - interval '1 hour'; END IF;
            EXIT;
        END IF;

        -- The Seller turns auto-renew off and the plan ends on its renewal date.
        IF NOT p_annual AND random() < v_churn THEN
            v_renewal := v_next;
            v_auto := false;
            IF v_next < v_today - 2 THEN
                v_end_state := 'expired';
                v_end_at := v_next::timestamptz;
            ELSE
                v_end_state := 'active';
                v_end_at := NULL;
            END IF;
            EXIT;
        END IF;

        IF v_next > v_today THEN
            v_renewal := v_next;
            v_end_state := 'active';
            v_end_at := NULL;
            EXIT;
        END IF;
        v_bill := v_next;
    END LOOP;

    -- Invoices not paid through PayOS went through Stripe.
    UPDATE invoices SET stripe_invoice_id = 'in_demo_' || substr(md5(id::text), 1, 14)
    WHERE subscription_id = v_sub AND payos_order_code IS NULL AND status <> 'void';

    UPDATE subscriptions
    SET status = v_end_state,
        renewal_date = v_renewal,
        auto_renew = v_auto AND v_end_state = 'active',
        cancelled_at = v_end_at,
        updated_at = coalesce(v_end_at, p_at)
    WHERE id = v_sub;
END $$;

DO $$
DECLARE
    v_now         timestamptz := now();
    -- Monday 14 weeks before this one: the forecast skips the running week and needs 14 whole ones.
    v_first_day   date := date_trunc('week', current_date)::date - 98;
    v_seller_role uuid := (SELECT id FROM roles WHERE code = 'Seller');
    v_pro         record;
    v_week        int;
    v_new_count   int;
    v_at          timestamptz;
    v_pick        numeric;
    v_path        text;
    v_customer_no int := 0;
    v_user_id     uuid;
    v_trial_end   timestamptz;
    v_convert_at  timestamptz;
    v_converted   text;
    v_first_names text[] := ARRAY['An','Bình','Châu','Dũng','Giang','Hà','Hải','Hạnh','Hiếu','Hoa','Hùng','Huy',
                                  'Khánh','Lan','Linh','Long','Mai','Minh','My','Nam','Ngân','Ngọc','Nhung','Phúc',
                                  'Phương','Quân','Quỳnh','Sơn','Tâm','Thảo','Thành','Trang','Trung','Tú','Vy','Yến'];
    v_last_names  text[] := ARRAY['Nguyễn','Trần','Lê','Phạm','Hoàng','Huỳnh','Phan','Vũ','Võ','Đặng','Bùi','Đỗ',
                                  'Hồ','Ngô','Dương','Lý'];
BEGIN
    PERFORM setseed(0.3434);
    SELECT * INTO v_pro FROM subscription_plans WHERE tier = 'pro';
    IF v_seller_role IS NULL OR v_pro.id IS NULL THEN
        RAISE EXCEPTION 'Needs the Seller role and the pro plan: run subscription-entitlements.sql first.';
    END IF;

    FOR v_week IN 0..14 LOOP
        v_new_count := greatest(20, round((35 + 55.0 * v_week / 14) * (0.8 + random() * 0.4)));

        FOR i IN 1..v_new_count LOOP
            v_at := v_first_day + v_week * 7 + random() * interval '7 days';
            v_at := date_trunc('day', v_at) + interval '7 hours' + random() * interval '14 hours';
            CONTINUE WHEN v_at > v_now;

            v_customer_no := v_customer_no + 1;
            INSERT INTO users (email, password_hash, full_name, account_status, email_verified, email_verified_at,
                               created_at, updated_at)
            VALUES (format('customer%s@apcs-demo.local', lpad(v_customer_no::text, 4, '0')), 'demo-account-no-login',
                    v_last_names[1 + floor(random() * array_length(v_last_names, 1))::int] || ' '
                        || v_first_names[1 + floor(random() * array_length(v_first_names, 1))::int],
                    'active', true, v_at + interval '5 minutes', v_at, v_at)
            RETURNING id INTO v_user_id;
            INSERT INTO user_roles (user_id, role_id, granted_at) VALUES (v_user_id, v_seller_role, v_at);

            v_pick := random();
            -- In the launch weeks more early adopters buy right away instead of trying first.
            v_path := CASE
                WHEN v_week <= 3 THEN CASE WHEN v_pick < 0.50 THEN 'free' WHEN v_pick < 0.75 THEN 'trial'
                                           WHEN v_pick < 0.87 THEN 'starter' ELSE 'pro' END
                ELSE CASE WHEN v_pick < 0.50 THEN 'free' WHEN v_pick < 0.80 THEN 'trial'
                          WHEN v_pick < 0.94 THEN 'starter' ELSE 'pro' END
            END;

            IF v_path IN ('starter', 'pro') THEN
                -- Buys on the day of sign-up, an hour or two after looking around.
                PERFORM pg_temp.demo_paid_subscription(v_user_id, v_path,
                    least(v_at + interval '1 hour' + random() * interval '3 hours', v_now), nextval('demo_purchase_no') % 10 = 0);
            ELSIF v_path = 'trial' THEN
                -- The trial starts within a day of sign-up and needs the Seller's Gemini key.
                v_at := least(v_at + random() * interval '1 day', v_now);
                v_trial_end := v_at + interval '14 days';
                v_convert_at := v_at + interval '4 days' + random() * interval '9 days 20 hours';
                v_pick := random();
                v_converted := CASE WHEN v_convert_at > v_now THEN NULL
                                    WHEN v_pick < 0.30 THEN 'pro' WHEN v_pick < 0.40 THEN 'starter' END;

                INSERT INTO subscriptions (user_id, plan_id, billing_cycle, monthly_price_usd, status, start_date,
                                           renewal_date, trial_ends_at, auto_renew, cancelled_at, created_at, updated_at)
                VALUES (v_user_id, v_pro.id, 'monthly', 0,
                        CASE WHEN v_converted IS NOT NULL THEN 'cancelled'
                             WHEN v_trial_end <= v_now THEN 'expired' ELSE 'active' END,
                        v_at::date, v_trial_end::date, v_trial_end, false,
                        CASE WHEN v_converted IS NOT NULL THEN v_convert_at END,
                        v_at, CASE WHEN v_converted IS NOT NULL THEN v_convert_at
                                   WHEN v_trial_end <= v_now THEN v_trial_end ELSE v_at END);

                IF v_converted IS NOT NULL THEN
                    PERFORM pg_temp.demo_paid_subscription(v_user_id, v_converted, v_convert_at, nextval('demo_purchase_no') % 10 = 0);
                END IF;
            END IF;
        END LOOP;
    END LOOP;
END $$;

-- Two Pro customers downgrade to Starter at their next renewal.
UPDATE subscriptions s
SET scheduled_plan_id = (SELECT id FROM subscription_plans WHERE tier = 'starter'),
    scheduled_plan_effective_date = s.renewal_date
WHERE s.id IN (
    SELECT s2.id FROM subscriptions s2
    JOIN subscription_plans p ON p.id = s2.plan_id JOIN users u ON u.id = s2.user_id
    WHERE u.email LIKE '%@apcs-demo.local' AND p.tier = 'pro' AND s2.status = 'active' AND s2.auto_renew
      AND s2.trial_ends_at IS NULL AND s2.renewal_date BETWEEN current_date + 3 AND current_date + 30
    ORDER BY u.email DESC LIMIT 2);

-- Make sure a few renewals failed in the last three days, so the past-due alert has rows: the
-- renewal invoice of four monthly customers who just renewed did not go through.
WITH picked AS (
    SELECT s.id, (s.renewal_date - interval '1 month')::date AS bill
    FROM subscriptions s JOIN users u ON u.id = s.user_id
    WHERE u.email LIKE '%@apcs-demo.local' AND s.status = 'active' AND s.billing_cycle = 'monthly'
      AND s.trial_ends_at IS NULL AND s.scheduled_plan_id IS NULL
      AND (s.renewal_date - interval '1 month')::date BETWEEN current_date - 2 AND current_date
      AND s.start_date < (s.renewal_date - interval '1 month')::date
    ORDER BY u.email LIMIT 4
), failed AS (
    UPDATE invoices i SET status = 'overdue', payment_date = NULL
    FROM picked p WHERE i.subscription_id = p.id AND i.invoice_date = p.bill
    RETURNING i.subscription_id
)
UPDATE subscriptions s SET status = 'past_due', renewal_date = p.bill, auto_renew = true
FROM picked p WHERE s.id = p.id AND s.id IN (SELECT subscription_id FROM failed);

-- Two Free customers opened a PayOS checkout one and two days ago and have not paid yet.
DO $$
DECLARE
    v_customer record;
    v_plan     record;
    v_sub      uuid;
    v_at       timestamptz;
    n          int := 0;
BEGIN
    FOR v_customer IN
        SELECT u.id FROM users u
        WHERE u.email LIKE '%@apcs-demo.local'
          AND NOT EXISTS (SELECT 1 FROM subscriptions s WHERE s.user_id = u.id)
        ORDER BY u.created_at LIMIT 2
    LOOP
        n := n + 1;
        SELECT * INTO v_plan FROM subscription_plans WHERE tier = CASE n WHEN 1 THEN 'pro' ELSE 'starter' END;
        v_at := now() - make_interval(days => n, hours => 3);
        INSERT INTO subscriptions (user_id, plan_id, billing_cycle, monthly_price_usd, annual_price_usd, status,
                                   start_date, renewal_date, auto_renew, created_at, updated_at)
        VALUES (v_customer.id, v_plan.id, 'monthly', v_plan.monthly_price_usd, v_plan.annual_price_usd, 'trialing',
                v_at::date, (v_at + interval '1 month')::date, true, v_at, v_at)
        RETURNING id INTO v_sub;
        INSERT INTO invoices (subscription_id, user_id, invoice_number, invoice_date, due_date, amount_usd, tax_amount,
                              total_amount, status, items, payos_order_code, created_at, updated_at)
        VALUES (v_sub, v_customer.id, 'INV-' || to_char(v_at, 'YYYYMMDD') || '-' || upper(substr(md5(random()::text), 1, 8)),
                v_at::date, v_at::date, v_plan.monthly_price_usd, 0, v_plan.monthly_price_usd, 'issued',
                jsonb_build_array(jsonb_build_object('description', v_plan.name || ' plan (monthly)', 'amount', v_plan.monthly_price_usd)),
                nextval('demo_order_code'), v_at, v_at);
    END LOOP;
END $$;

COMMIT;

-- Summary printed by the runner script: paying customers and MRR today, per plan.
SELECT p.name AS plan,
       count(*) FILTER (WHERE s.status = 'active')                                   AS active,
       count(*) FILTER (WHERE s.status = 'past_due')                                 AS past_due,
       count(*) FILTER (WHERE s.status = 'expired')                                  AS ended,
       round(sum(CASE WHEN s.billing_cycle = 'annual' THEN s.annual_price_usd / 12 ELSE s.monthly_price_usd END)
             FILTER (WHERE s.status = 'active'), 2)                                  AS mrr_usd
FROM subscriptions s
JOIN subscription_plans p ON p.id = s.plan_id
JOIN users u ON u.id = s.user_id
WHERE u.email LIKE '%@apcs-demo.local' AND s.trial_ends_at IS NULL AND s.status <> 'trialing'
GROUP BY p.name, p.sort_order
ORDER BY p.sort_order;
