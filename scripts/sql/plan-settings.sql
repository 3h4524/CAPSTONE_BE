-- What an Admin sets for Free and the free trial, which are not plans in subscription_plans.
--
--   usage_allowances  Free's monthly allowances and the trial's allowances for its whole length.
--                     Seeded with the values the backend shipped with (the UsageAllowances section
--                     of appsettings.json); a missing row falls back to those.
--   plan_settings     Other settings by key. trial_plan_tier names the plan the free trial gives;
--                     without it the backend uses 'pro'.
--
-- The trial's length stays fixed in code (14 days): the trial allowances are counted back from its end.
--
-- Additive and safe to run more than once; never overwrites an Admin's values.
-- Review it against the target database before applying.

BEGIN;

CREATE TABLE IF NOT EXISTS usage_allowances (
    scope       varchar(10) NOT NULL,
    code        varchar(30) NOT NULL,
    limit_value integer     NOT NULL,
    updated_at  timestamptz,
    updated_by  uuid,
    CONSTRAINT pk_usage_allowances PRIMARY KEY (scope, code),
    CONSTRAINT ck_usage_allowances_scope CHECK (scope IN ('free', 'trial')),
    CONSTRAINT ck_usage_allowances_limit CHECK (limit_value >= 0),
    CONSTRAINT fk_usage_allowances_updated_by FOREIGN KEY (updated_by) REFERENCES users (id) ON DELETE SET NULL
);

INSERT INTO usage_allowances (scope, code, limit_value)
VALUES
    ('free', 'full_runs', 2),
    ('free', 'design_runs', 5),
    ('free', 'design_images', 7),
    ('free', 'mockups', 6),
    ('free', 'videos', 2),
    ('free', 'listings', 5),
    ('trial', 'products_per_run', 3),
    ('trial', 'products', 10),
    ('trial', 'design_images', 40),
    ('trial', 'mockups', 100),
    ('trial', 'videos', 10),
    ('trial', 'listings', 20)
ON CONFLICT (scope, code) DO NOTHING;

CREATE TABLE IF NOT EXISTS plan_settings (
    key        varchar(50) NOT NULL,
    value      text        NOT NULL,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT pk_plan_settings PRIMARY KEY (key),
    CONSTRAINT fk_plan_settings_updated_by FOREIGN KEY (updated_by) REFERENCES users (id) ON DELETE SET NULL
);

-- The trial gives Pro, as before, where a Pro plan exists.
INSERT INTO plan_settings (key, value)
SELECT 'trial_plan_tier', 'pro'
WHERE EXISTS (SELECT 1 FROM subscription_plans WHERE tier = 'pro')
ON CONFLICT (key) DO NOTHING;

COMMIT;
