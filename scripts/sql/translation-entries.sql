-- Apply this script to the Neon public schema before enabling persisted translations.
-- This repository is database-first: do not replace this with an EF migration.

CREATE TABLE IF NOT EXISTS public.translation_entries
(
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    source_hash varchar(64) NOT NULL,
    source_text text NOT NULL,
    source_locale varchar(10) NOT NULL,
    target_locale varchar(10) NOT NULL,
    translated_text text NOT NULL,
    provider varchar(40) NOT NULL,
    created_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT translation_entries_lookup_key
        UNIQUE (source_hash, source_locale, target_locale, provider)
);

CREATE INDEX IF NOT EXISTS idx_translation_entries_locale
    ON public.translation_entries (source_locale, target_locale);
