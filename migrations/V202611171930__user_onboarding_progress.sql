-- Per-user onboarding state: which page guides the Seller started, completed or skipped (with the
-- step to resume at), and whether they hid the dashboard checklist. One row per user, created
-- lazily on the first save.
CREATE TABLE IF NOT EXISTS public.user_onboarding_progress (
    user_id uuid PRIMARY KEY REFERENCES public.users(id) ON DELETE CASCADE,
    guide_progress jsonb NOT NULL DEFAULT '{}'::jsonb,
    checklist_dismissed boolean NOT NULL DEFAULT false,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);
