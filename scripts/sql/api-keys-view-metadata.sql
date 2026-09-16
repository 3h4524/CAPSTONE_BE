-- Add display/check metadata only; existing credentials and statuses are preserved.
BEGIN;
ALTER TABLE public.api_keys
    ADD COLUMN IF NOT EXISTS auth_type character varying(20) NOT NULL DEFAULT 'api_key',
    ADD COLUMN IF NOT EXISTS environment character varying(50),
    ADD COLUMN IF NOT EXISTS connected_account_name character varying(200),
    ADD COLUMN IF NOT EXISTS last_checked_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS last_check_succeeded boolean;
COMMENT ON COLUMN public.api_keys.last_check_succeeded IS 'NULL means no recorded connectivity result; written by the credential validation flow.';
COMMIT;
