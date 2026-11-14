-- Content moderation (guardrail), phase 1: the platform's own check of seller text before it reaches an image or video
-- provider, and of the images those providers return. See docs/CONTENT_MODERATION.md.

-- Words and phrases checked before Gemini. 'block' refuses the text outright; 'flag' only tells the Gemini check which
-- category to look at closely. Terms are matched on whole words of the normalized text, accents kept; a term with
-- match_unaccented is also matched once accents are removed, which is only safe when its accent-free spelling is not
-- an everyday word (Vietnamese "lồn" without accents is "lon", a can).
CREATE TABLE IF NOT EXISTS public.moderation_terms (
    id uuid DEFAULT gen_random_uuid() PRIMARY KEY,
    term text NOT NULL CHECK (btrim(term) <> '' AND term = lower(term)),
    category text NOT NULL CHECK (category IN
        ('sexual', 'sexual_minors', 'violence_gore', 'hate', 'self_harm', 'illegal', 'ip_trademark')),
    severity text NOT NULL CHECK (severity IN ('block', 'flag')),
    match_type text NOT NULL DEFAULT 'word' CHECK (match_type IN ('word', 'phrase')),
    match_unaccented boolean NOT NULL DEFAULT false,
    language character varying(5) NOT NULL DEFAULT 'en',
    is_active boolean NOT NULL DEFAULT true,
    note text NULL,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by uuid NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_moderation_terms_term ON public.moderation_terms (term);

-- Harmless phrases that contain a listed term ("naked eye", "killer whale"): masked before the terms are matched.
CREATE TABLE IF NOT EXISTS public.moderation_allowlist (
    id uuid DEFAULT gen_random_uuid() PRIMARY KEY,
    phrase text NOT NULL CHECK (btrim(phrase) <> '' AND phrase = lower(phrase)),
    is_active boolean NOT NULL DEFAULT true,
    note text NULL,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by uuid NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_moderation_allowlist_phrase ON public.moderation_allowlist (phrase);

-- One row per moderation decision that was actually evaluated (a cached verdict is not written again). It is the
-- audit trail, the source of statistics and, for an output image, its moderation state: the latest row for an image
-- decides whether it may be approved. No foreign key to users, so the trail outlives the account. content_excerpt keeps
-- the seller's text (at most 500 characters) only for a text check that was not allowed, for an administrator to review.
CREATE TABLE IF NOT EXISTS public.moderation_checks (
    id uuid DEFAULT gen_random_uuid() PRIMARY KEY,
    user_id uuid NULL,
    feature text NOT NULL CHECK (btrim(feature) <> ''),
    direction text NOT NULL CHECK (direction IN ('input', 'output')),
    subject_type text NOT NULL CHECK (btrim(subject_type) <> ''),
    subject_id uuid NULL,
    content_hash character(64) NOT NULL,
    decision text NOT NULL CHECK (decision IN ('allow', 'flagged', 'block', 'unchecked')),
    decided_by text NOT NULL CHECK (decided_by IN ('blocklist', 'gemini', 'provider_safety', 'error', 'disabled')),
    categories jsonb NOT NULL DEFAULT '[]'::jsonb,
    reason text NULL,
    content_excerpt text NULL CHECK (content_excerpt IS NULL OR char_length(content_excerpt) <= 500),
    model text NULL,
    latency_ms integer NULL CHECK (latency_ms IS NULL OR latency_ms >= 0),
    policy_version integer NOT NULL CHECK (policy_version > 0),
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_moderation_checks_subject
    ON public.moderation_checks (subject_type, subject_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_moderation_checks_user
    ON public.moderation_checks (user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_moderation_checks_created
    ON public.moderation_checks (created_at DESC);

CREATE INDEX IF NOT EXISTS ix_moderation_checks_decider
    ON public.moderation_checks (decided_by, created_at DESC);

-- The guardrail's settings an administrator can change at runtime: 'enabled' ('true'/'false') switches the Gemini
-- check, and 'block_at.<category>' ('high' or 'medium') is the lowest rating that blocks in that category. Minors in a
-- sexual context always block and have no setting.
CREATE TABLE IF NOT EXISTS public.moderation_settings (
    key text PRIMARY KEY CHECK (key = 'enabled' OR key IN ('block_at.sexual', 'block_at.violence_gore', 'block_at.hate',
        'block_at.self_harm', 'block_at.illegal', 'block_at.ip_trademark')),
    value text NOT NULL CHECK ((key = 'enabled' AND value IN ('true', 'false')) OR (key <> 'enabled' AND value IN ('high', 'medium'))),
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by uuid NULL
);

INSERT INTO public.moderation_settings (key, value) VALUES
    ('enabled', 'true'),
    ('block_at.sexual', 'high'),
    ('block_at.violence_gore', 'high'),
    ('block_at.hate', 'high'),
    ('block_at.self_harm', 'high'),
    ('block_at.illegal', 'high'),
    ('block_at.ip_trademark', 'high')
ON CONFLICT DO NOTHING;

-- A starting list. Unambiguous terms block; terms with harmless uses ("nude" is also a color) only flag, so Gemini
-- decides. Extend it with a new migration.
INSERT INTO public.moderation_terms (term, category, severity, match_type, match_unaccented, language, note) VALUES
    -- Minors in a sexual context: never allowed.
    ('child porn', 'sexual_minors', 'block', 'phrase', false, 'en', NULL),
    ('child pornography', 'sexual_minors', 'block', 'phrase', false, 'en', NULL),
    ('underage sex', 'sexual_minors', 'block', 'phrase', false, 'en', NULL),
    ('underage nude', 'sexual_minors', 'block', 'phrase', false, 'en', NULL),
    ('preteen nude', 'sexual_minors', 'block', 'phrase', false, 'en', NULL),
    ('pedophile', 'sexual_minors', 'block', 'word', false, 'en', NULL),
    ('pedophilia', 'sexual_minors', 'block', 'word', false, 'en', NULL),
    ('lolicon', 'sexual_minors', 'block', 'word', false, 'en', NULL),
    ('shotacon', 'sexual_minors', 'block', 'word', false, 'en', NULL),
    ('ấu dâm', 'sexual_minors', 'block', 'phrase', true, 'vi', 'Accent-free "au dam" is not an everyday phrase.'),
    ('khiêu dâm trẻ em', 'sexual_minors', 'block', 'phrase', true, 'vi', NULL),
    ('loli', 'sexual_minors', 'flag', 'word', false, 'en', 'Also a fashion style name; Gemini decides.'),
    -- Explicit sexual content.
    ('porn', 'sexual', 'block', 'word', false, 'en', NULL),
    ('porno', 'sexual', 'block', 'word', false, 'en', NULL),
    ('pornographic', 'sexual', 'block', 'word', false, 'en', NULL),
    ('hentai', 'sexual', 'block', 'word', false, 'en', NULL),
    ('blowjob', 'sexual', 'block', 'word', false, 'en', NULL),
    ('gangbang', 'sexual', 'block', 'word', false, 'en', NULL),
    ('khiêu dâm', 'sexual', 'block', 'phrase', true, 'vi', NULL),
    ('địt', 'sexual', 'block', 'word', true, 'vi', 'Accent-free "dit" is matched as a whole word only.'),
    ('lồn', 'sexual', 'block', 'word', false, 'vi', 'Accent-free "lon" is "can"; matched with accents only.'),
    ('buồi', 'sexual', 'block', 'word', false, 'vi', 'Accent-free "buoi" is "buổi"; matched with accents only.'),
    ('cặc', 'sexual', 'block', 'word', false, 'vi', 'Accent-free "cac" is "các"; matched with accents only.'),
    ('nứng', 'sexual', 'block', 'word', false, 'vi', 'Accent-free "nung" is "nung" (to fire, as in nung gạch); matched with accents only.'),
    ('đụ', 'sexual', 'block', 'word', false, 'vi', 'Accent-free "du" is "du lịch"; matched with accents only.'),
    ('chịch', 'sexual', 'block', 'word', false, 'vi', 'Accent-free "chich" is "chích"; matched with accents only.'),
    ('bú cu', 'sexual', 'block', 'phrase', false, 'vi', NULL),
    ('xoạc', 'sexual', 'flag', 'word', false, 'vi', 'Also a split in dance or sport; Gemini decides.'),
    ('nude', 'sexual', 'flag', 'word', false, 'en', 'Also a color name.'),
    ('naked', 'sexual', 'flag', 'word', false, 'en', NULL),
    ('topless', 'sexual', 'flag', 'word', false, 'en', NULL),
    ('nsfw', 'sexual', 'flag', 'word', false, 'en', NULL),
    ('sex', 'sexual', 'flag', 'word', false, 'en', NULL),
    ('sexy', 'sexual', 'flag', 'word', false, 'en', NULL),
    ('khỏa thân', 'sexual', 'flag', 'phrase', true, 'vi', NULL),
    -- Violence and gore.
    ('gore', 'violence_gore', 'flag', 'word', false, 'en', NULL),
    ('beheading', 'violence_gore', 'flag', 'word', false, 'en', NULL),
    ('decapitated', 'violence_gore', 'flag', 'word', false, 'en', NULL),
    ('dismembered', 'violence_gore', 'flag', 'word', false, 'en', NULL),
    ('mass shooting', 'violence_gore', 'flag', 'phrase', false, 'en', NULL),
    ('school shooting', 'violence_gore', 'block', 'phrase', false, 'en', NULL),
    ('máu me', 'violence_gore', 'flag', 'phrase', false, 'vi', NULL),
    -- Hate.
    ('nigger', 'hate', 'block', 'word', false, 'en', NULL),
    ('faggot', 'hate', 'block', 'word', false, 'en', NULL),
    ('kike', 'hate', 'block', 'word', false, 'en', NULL),
    ('white power', 'hate', 'flag', 'phrase', false, 'en', NULL),
    ('nazi', 'hate', 'flag', 'word', false, 'en', 'Also anti-nazi or history designs; Gemini decides.'),
    ('swastika', 'hate', 'flag', 'word', false, 'en', NULL),
    ('kkk', 'hate', 'flag', 'word', false, 'en', NULL),
    -- Self-harm.
    ('suicide', 'self_harm', 'flag', 'word', false, 'en', 'Also awareness designs; Gemini decides.'),
    ('self harm', 'self_harm', 'flag', 'phrase', false, 'en', NULL),
    ('tự sát', 'self_harm', 'flag', 'phrase', false, 'vi', NULL),
    -- Illegal goods.
    ('cocaine', 'illegal', 'flag', 'word', false, 'en', NULL),
    ('meth', 'illegal', 'flag', 'word', false, 'en', NULL),
    ('heroin', 'illegal', 'flag', 'word', false, 'en', NULL),
    ('ma túy', 'illegal', 'flag', 'phrase', true, 'vi', NULL)
ON CONFLICT DO NOTHING;

INSERT INTO public.moderation_allowlist (phrase, note) VALUES
    ('naked eye', NULL),
    ('nude color', 'Fashion color name.'),
    ('nude colour', 'Fashion color name.'),
    ('nude tone', 'Fashion color name.'),
    ('nude lipstick', 'Fashion color name.'),
    ('suicide prevention', 'Awareness design.'),
    ('suicide awareness', 'Awareness design.'),
    ('anti nazi', 'Also matches "anti-nazi": phrases match across any punctuation.'),
    ('sex education', NULL)
ON CONFLICT DO NOTHING;
