-- The platform's own AI provider keys (Ideogram images, MiniMax video), managed by an Admin on /admin/system-api-keys
-- instead of the SystemApiKeys__* environment values. The key is encrypted with ASP.NET DataProtection, by a key ring of
-- its own kept in system_api_key_ring, so every API server on this database can read it.

-- Each provider's limits: how far each of its keys is used in a day and at a time, and in what order.
CREATE TABLE IF NOT EXISTS public.system_api_key_providers (
    provider text PRIMARY KEY CHECK (provider IN ('ideogram', 'minimax')),
    daily_limit_per_key integer NULL CHECK (daily_limit_per_key >= 0),
    max_concurrent_per_key integer NULL CHECK (max_concurrent_per_key > 0),
    strategy varchar(10) NOT NULL DEFAULT 'fill' CHECK (strategy IN ('fill', 'rotate')),
    updated_by uuid NULL REFERENCES public.users(id) ON DELETE SET NULL,
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- One platform key. key_id is the first 12 hex digits of the key's SHA-256: the id its daily use is counted under in
-- Redis, shown without decrypting the key and used to refuse the same key twice.
CREATE TABLE IF NOT EXISTS public.system_api_keys (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider text NOT NULL REFERENCES public.system_api_key_providers(provider),
    label varchar(100) NOT NULL,
    key_value_encrypted text NOT NULL,
    key_last4 varchar(4) NOT NULL,
    key_id varchar(12) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    last_checked_at timestamp with time zone NULL,
    last_check_succeeded boolean NULL,
    updated_by uuid NULL REFERENCES public.users(id) ON DELETE SET NULL,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_system_api_keys_provider_key_id
    ON public.system_api_keys (provider, key_id)
    WHERE deleted_at IS NULL;

-- The DataProtection keys that encrypt system_api_keys, one XML element per row, and nothing else.
CREATE TABLE IF NOT EXISTS public.system_api_key_ring (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    friendly_name text NULL,
    xml text NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);

INSERT INTO public.system_api_key_providers (provider, daily_limit_per_key, max_concurrent_per_key, strategy)
VALUES ('ideogram', 500, 8, 'fill'),
       ('minimax', 100, NULL, 'fill')
ON CONFLICT (provider) DO NOTHING;
