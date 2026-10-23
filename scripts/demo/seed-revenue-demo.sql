-- Revenue demo data: about 18 months of customers, subscriptions and invoices so the revenue
-- dashboard and its weekly OLS forecast have enough history to show a real trend.
--
-- ONLY for the throwaway demo database that scripts/demo/revenue-demo.sh starts in Docker. Every
-- demo customer uses the @apcs-demo.local domain, which is how a reseed finds and removes them.
--
-- The data is generated, not copied: new customers per month grow linearly with noise and a
-- seasonal bump (Q4 up, February/Tet down), each customer renews monthly or yearly until a
-- per-plan churn draw ends it, and a few invoices fail, get refunded or are abandoned at checkout.
-- setseed() makes every run produce the same rows.

\set ON_ERROR_STOP on

BEGIN;

-- Reference rows, only when the copy from the team database did not provide them.
INSERT INTO roles (code, name, description)
SELECT v.code, v.code, v.code || ' role'
FROM (VALUES ('Seller'), ('Admin')) AS v(code)
WHERE NOT EXISTS (SELECT 1 FROM roles r WHERE r.code = v.code);

INSERT INTO subscription_plans (name, tier, monthly_price_usd, annual_price_usd, sort_order)
SELECT v.name, v.tier, v.monthly, v.annual, v.sort_order
FROM (VALUES
    ('Starter', 'starter', 9.99, 99.99, 1),
    ('Creator', 'creator', 29.99, 299.99, 2),
    ('Studio', 'studio', 79.99, 799.99, 3)
) AS v(name, tier, monthly, annual, sort_order)
WHERE NOT EXISTS (SELECT 1 FROM subscription_plans p WHERE p.name = v.name);

DO $$
DECLARE
    v_today         date := current_date;
    v_first_month   date := (date_trunc('month', current_date) - interval '17 months')::date;
    v_seller_role   uuid;
    v_month         date;
    v_month_index   int := 0;
    v_new_count     int;
    v_seasonal      numeric;
    v_customer_no   int := 0;
    v_user_id       uuid;
    v_sub_id        uuid;
    v_plan          record;
    v_pick          numeric;
    v_plan_name     text;
    v_churn_p       numeric;
    v_period_churn  numeric;
    v_is_annual     boolean;
    v_started_at    timestamptz;
    v_start_date    date;
    v_bill_date     date;
    v_next_date     date;
    v_price         numeric(10, 2);
    v_status        text;
    v_cancelled_at  timestamptz;
    v_paid_count    int;
    v_first_names   text[] := ARRAY['An','Bình','Châu','Dũng','Giang','Hà','Hải','Hạnh','Hiếu','Hoa','Hùng','Huy',
                                    'Khánh','Lan','Linh','Long','Mai','Minh','My','Nam','Ngân','Ngọc','Nhung','Phúc',
                                    'Phương','Quân','Quỳnh','Sơn','Tâm','Thảo','Thành','Trang','Trung','Tú','Vy','Yến'];
    v_last_names    text[] := ARRAY['Nguyễn','Trần','Lê','Phạm','Hoàng','Huỳnh','Phan','Vũ','Võ','Đặng','Bùi','Đỗ',
                                    'Hồ','Ngô','Dương','Lý'];
    v_first         text;
    v_last          text;
BEGIN
    PERFORM setseed(0.4242);

    SELECT id INTO v_seller_role FROM roles WHERE code = 'Seller';

    v_month := v_first_month;
    WHILE v_month <= v_today LOOP
        -- Q4 is the busy season for online sellers; February holds Tet.
        v_seasonal := CASE extract(month FROM v_month)
                          WHEN 11 THEN 1.30 WHEN 12 THEN 1.35 WHEN 10 THEN 1.10
                          WHEN 2 THEN 0.80 ELSE 1.0 END;
        v_new_count := greatest(1, round((4 + 2.2 * v_month_index) * v_seasonal + (random() - 0.5) * 6));

        FOR i IN 1..v_new_count LOOP
            v_started_at := v_month + (random() * (((v_month + interval '1 month')::date - v_month))) * interval '1 day'
                            + (random() * interval '14 hours') + interval '7 hours';
            CONTINUE WHEN v_started_at::date > v_today;
            v_start_date := v_started_at::date;

            -- Customer account. It has no usable password: demo customers never sign in.
            v_customer_no := v_customer_no + 1;
            v_first := v_first_names[1 + floor(random() * array_length(v_first_names, 1))::int];
            v_last := v_last_names[1 + floor(random() * array_length(v_last_names, 1))::int];
            INSERT INTO users (email, password_hash, full_name, account_status, email_verified,
                               email_verified_at, created_at, updated_at)
            VALUES (format('customer%s@apcs-demo.local', lpad(v_customer_no::text, 4, '0')),
                    'demo-account-no-login', v_last || ' ' || v_first, 'active', true,
                    v_started_at - interval '20 minutes', v_started_at - interval '30 minutes', v_started_at)
            RETURNING id INTO v_user_id;

            INSERT INTO user_roles (user_id, role_id, granted_at)
            VALUES (v_user_id, v_seller_role, v_started_at - interval '30 minutes');

            -- Plan mix: Starter 50%, Creator 35%, Studio 15%; 8% pay yearly. Bigger plans churn less.
            v_pick := random();
            v_plan_name := CASE WHEN v_pick < 0.50 THEN 'Starter' WHEN v_pick < 0.85 THEN 'Creator' ELSE 'Studio' END;
            v_churn_p := CASE v_plan_name WHEN 'Starter' THEN 0.06 WHEN 'Creator' THEN 0.04 ELSE 0.03 END;
            SELECT * INTO v_plan FROM subscription_plans WHERE name = v_plan_name;
            v_is_annual := random() < 0.08;
            v_period_churn := CASE WHEN v_is_annual THEN v_churn_p * 3 ELSE v_churn_p END;
            v_price := CASE WHEN v_is_annual THEN coalesce(v_plan.annual_price_usd, v_plan.monthly_price_usd * 10)
                            ELSE v_plan.monthly_price_usd END;

            INSERT INTO subscriptions (user_id, plan_id, billing_cycle, monthly_price_usd, annual_price_usd,
                                       status, start_date, renewal_date, created_at, updated_at)
            VALUES (v_user_id, v_plan.id, CASE WHEN v_is_annual THEN 'annual' ELSE 'monthly' END,
                    v_plan.monthly_price_usd, v_plan.annual_price_usd, 'active', v_start_date, v_start_date,
                    v_started_at, v_started_at)
            RETURNING id INTO v_sub_id;

            -- About 6% abandon the checkout: one cancelled (void) invoice, never paid, sub expired.
            IF random() < 0.06 THEN
                INSERT INTO invoices (subscription_id, user_id, invoice_number, invoice_date, due_date, amount_usd,
                                      tax_amount, total_amount, status, items, payos_order_code, created_at, updated_at)
                VALUES (v_sub_id, v_user_id, 'INV-' || to_char(v_start_date, 'YYYYMMDD') || '-' || upper(substr(md5(random()::text), 1, 8)),
                        v_start_date, v_start_date, v_price, 0, v_price, 'void',
                        jsonb_build_array(jsonb_build_object('description', v_plan_name || ' plan', 'amount', v_price)),
                        (1000000 + floor(random() * 8999999))::bigint, v_started_at, v_started_at);
                UPDATE subscriptions SET status = 'expired', auto_renew = false, cancelled_at = v_started_at + interval '15 minutes'
                WHERE id = v_sub_id;
                CONTINUE;
            END IF;

            -- Billing periods until churn or today.
            v_bill_date := v_start_date;
            v_cancelled_at := NULL;
            v_paid_count := 0;
            LOOP
                v_next_date := (v_bill_date + CASE WHEN v_is_annual THEN interval '1 year' ELSE interval '1 month' END)::date;

                v_pick := random();
                -- The first payment always succeeds (it is what opens the subscription).
                v_status := CASE WHEN v_paid_count = 0 THEN 'paid'
                                 WHEN v_pick < 0.03 THEN 'overdue'
                                 WHEN v_pick < 0.045 THEN 'refunded'
                                 ELSE 'paid' END;

                INSERT INTO invoices (subscription_id, user_id, invoice_number, invoice_date, due_date, amount_usd,
                                      tax_amount, total_amount, status, payment_date, items, payos_order_code,
                                      stripe_invoice_id, created_at, updated_at)
                VALUES (v_sub_id, v_user_id,
                        'INV-' || to_char(v_bill_date, 'YYYYMMDD') || '-' || upper(substr(md5(random()::text), 1, 8)),
                        v_bill_date, v_bill_date + 3, v_price, 0, v_price, v_status,
                        CASE WHEN v_status IN ('paid', 'refunded') THEN v_bill_date END,
                        jsonb_build_array(jsonb_build_object(
                            'description', v_plan_name || CASE WHEN v_is_annual THEN ' plan (annual)' ELSE ' plan (monthly)' END,
                            'amount', v_price)),
                        CASE WHEN random() < 0.85 THEN (1000000 + floor(random() * 8999999))::bigint END,
                        NULL,
                        v_bill_date + (v_started_at - v_start_date::timestamptz),
                        v_bill_date + (v_started_at - v_start_date::timestamptz));

                IF v_status IN ('paid', 'refunded') THEN
                    v_paid_count := v_paid_count + 1;
                END IF;

                -- A refund ends the subscription; a failed renewal ends it half of the time; otherwise
                -- the plan's churn rate decides (annual customers churn per year, not per month).
                IF v_status = 'refunded'
                   OR (v_status = 'overdue' AND random() < 0.5)
                   OR random() < v_period_churn THEN
                    v_cancelled_at := (v_next_date - (1 + floor(random() * 10))::int)
                                      + (random() * interval '10 hours') + interval '8 hours';
                    IF v_cancelled_at::date > v_today THEN
                        v_cancelled_at := NULL;  -- the cancellation would be in the future: still active
                    END IF;
                    EXIT;
                END IF;

                EXIT WHEN v_next_date > v_today;
                v_bill_date := v_next_date;
            END LOOP;

            -- Invoices not paid through PayOS went through Stripe.
            UPDATE invoices
            SET stripe_invoice_id = 'in_demo_' || substr(md5(id::text), 1, 14)
            WHERE subscription_id = v_sub_id AND payos_order_code IS NULL AND status <> 'void';

            UPDATE subscriptions
            SET status = CASE WHEN v_cancelled_at IS NULL THEN 'active' ELSE 'cancelled' END,
                cancelled_at = v_cancelled_at,
                auto_renew = v_cancelled_at IS NULL,
                renewal_date = v_next_date,
                updated_at = coalesce(v_cancelled_at, v_started_at)
            WHERE id = v_sub_id;
        END LOOP;

        v_month := (v_month + interval '1 month')::date;
        v_month_index := v_month_index + 1;
    END LOOP;
END $$;

-- A few situations the Financial Alerts card watches for, so each rule has something to show.

-- Unpaid renewals: three monthly customers whose latest payment never came. Their renewal date moves
-- back to that missing payment, which is deleted.
WITH picked AS (
    SELECT s.id
    FROM subscriptions s JOIN users u ON u.id = s.user_id
    WHERE u.email LIKE '%@apcs-demo.local' AND s.status = 'active' AND s.billing_cycle = 'monthly'
      AND s.start_date < current_date - 60
    ORDER BY u.email
    LIMIT 3
), latest AS (
    SELECT DISTINCT ON (i.subscription_id) i.id, i.subscription_id, i.invoice_date
    FROM invoices i JOIN picked p ON p.id = i.subscription_id
    WHERE i.status = 'paid'
    ORDER BY i.subscription_id, i.invoice_date DESC
), removed AS (
    DELETE FROM invoices i USING latest l WHERE i.id = l.id RETURNING l.subscription_id, l.invoice_date
)
UPDATE subscriptions s SET renewal_date = r.invoice_date
FROM removed r WHERE s.id = r.subscription_id AND r.invoice_date < current_date;

-- Scheduled downgrades: two Creator/Studio customers moving to Starter at their next renewal.
UPDATE subscriptions s
SET scheduled_plan_id = (SELECT id FROM subscription_plans WHERE name = 'Starter'),
    scheduled_plan_effective_date = s.renewal_date
WHERE s.id IN (
    SELECT s2.id
    FROM subscriptions s2
    JOIN users u ON u.id = s2.user_id
    JOIN subscription_plans p ON p.id = s2.plan_id
    WHERE u.email LIKE '%@apcs-demo.local' AND s2.status = 'active' AND p.name IN ('Creator', 'Studio')
      AND s2.renewal_date BETWEEN current_date AND current_date + 30
    ORDER BY u.email DESC
    LIMIT 2
);

-- Unconfirmed checkouts: two PayOS links issued two and three days ago and never confirmed.
DO $$
DECLARE
    v_seller_role uuid := (SELECT id FROM roles WHERE code = 'Seller');
    v_plan        record;
    v_user_id     uuid;
    v_sub_id      uuid;
    v_at          timestamptz;
BEGIN
    FOR n IN 1..2 LOOP
        SELECT * INTO v_plan FROM subscription_plans WHERE name = CASE n WHEN 1 THEN 'Creator' ELSE 'Starter' END;
        v_at := now() - make_interval(days => n + 1);

        INSERT INTO users (email, password_hash, full_name, account_status, email_verified, created_at, updated_at)
        VALUES (format('checkout%s@apcs-demo.local', n), 'demo-account-no-login',
                CASE n WHEN 1 THEN 'Trần Khoa' ELSE 'Lê Thu' END, 'active', true, v_at, v_at)
        RETURNING id INTO v_user_id;
        INSERT INTO user_roles (user_id, role_id, granted_at) VALUES (v_user_id, v_seller_role, v_at);

        INSERT INTO subscriptions (user_id, plan_id, billing_cycle, monthly_price_usd, annual_price_usd, status,
                                   start_date, renewal_date, created_at, updated_at)
        VALUES (v_user_id, v_plan.id, 'monthly', v_plan.monthly_price_usd, v_plan.annual_price_usd, 'trialing',
                v_at::date, (v_at + interval '1 month')::date, v_at, v_at)
        RETURNING id INTO v_sub_id;

        INSERT INTO invoices (subscription_id, user_id, invoice_number, invoice_date, due_date, amount_usd, tax_amount,
                              total_amount, status, items, payos_order_code, created_at, updated_at)
        VALUES (v_sub_id, v_user_id, 'INV-' || to_char(v_at, 'YYYYMMDD') || '-' || upper(substr(md5(random()::text), 1, 8)),
                v_at::date, v_at::date, v_plan.monthly_price_usd, 0, v_plan.monthly_price_usd, 'issued',
                jsonb_build_array(jsonb_build_object('description', v_plan.name || ' plan (monthly)', 'amount', v_plan.monthly_price_usd)),
                (1000000 + floor(random() * 8999999))::bigint, v_at, v_at);
    END LOOP;
END $$;

COMMIT;

-- Summary printed by the runner script.
SELECT to_char(date_trunc('month', coalesce(i.payment_date, i.invoice_date)), 'YYYY-MM') AS month,
       count(*) FILTER (WHERE i.status = 'paid')                    AS paid_invoices,
       sum(i.total_amount) FILTER (WHERE i.status = 'paid')         AS paid_revenue
FROM invoices i
JOIN users u ON u.id = i.user_id
WHERE u.email LIKE '%@apcs-demo.local'
GROUP BY 1
ORDER BY 1;
