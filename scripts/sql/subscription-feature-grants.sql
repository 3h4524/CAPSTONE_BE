-- Features a subscriber keeps until the end of the period they paid for, after an Admin takes the
-- feature off their plan or lowers its limit.
--
-- A row holds what the subscriber had before the change. It counts only while the subscription is
-- still on plan_id and expires_on is in the future: renewing moves the subscription to a new period,
-- changing plan moves it off plan_id, and either way the row stops counting. SubscriptionRenewalJob
-- deletes rows that no longer count. Putting the feature back on the plan deletes them too.
--
-- Additive and safe to run more than once. Needs plan-feature-catalog.sql first.
-- Review it against the target database before applying.

BEGIN;

CREATE TABLE IF NOT EXISTS subscription_feature_grants (
    id              uuid         NOT NULL DEFAULT gen_random_uuid(),
    subscription_id uuid         NOT NULL,
    plan_id         uuid         NOT NULL,
    feature_code    varchar(100) NOT NULL,
    is_enabled      boolean      NOT NULL,
    limit_value     integer,
    expires_on      date         NOT NULL,
    reason          varchar(30)  NOT NULL,
    created_by      uuid,
    created_at      timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT pk_subscription_feature_grants PRIMARY KEY (id),
    CONSTRAINT uq_subscription_feature_grants_subscription_feature UNIQUE (subscription_id, feature_code),
    CONSTRAINT ck_subscription_feature_grants_reason CHECK (reason IN ('feature_removed', 'limit_lowered')),
    CONSTRAINT ck_subscription_feature_grants_limit CHECK (limit_value IS NULL OR limit_value >= 0),
    CONSTRAINT fk_subscription_feature_grants_subscription FOREIGN KEY (subscription_id) REFERENCES subscriptions (id) ON DELETE CASCADE,
    CONSTRAINT fk_subscription_feature_grants_plan FOREIGN KEY (plan_id) REFERENCES subscription_plans (id) ON DELETE CASCADE,
    CONSTRAINT fk_subscription_feature_grants_feature FOREIGN KEY (feature_code) REFERENCES features (code) ON UPDATE CASCADE,
    CONSTRAINT fk_subscription_feature_grants_created_by FOREIGN KEY (created_by) REFERENCES users (id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_subscription_feature_grants_expires_on
    ON subscription_feature_grants (expires_on);

CREATE INDEX IF NOT EXISTS idx_subscription_feature_grants_plan_feature
    ON subscription_feature_grants (plan_id, feature_code);

COMMIT;
