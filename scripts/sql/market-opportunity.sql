BEGIN;

-- Durable public-market observations. RAG metadata is not a substitute for a time series.
CREATE TABLE IF NOT EXISTS public.market_listing_snapshots (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    platform varchar(40) NOT NULL,
    external_listing_id varchar(200) NOT NULL,
    external_seller_id varchar(200) NULL,
    title varchar(500) NOT NULL,
    description text NULL,
    source_url text NULL,
    price numeric(14, 2) NULL,
    currency varchar(12) NULL,
    quantity integer NULL,
    favorites integer NULL,
    views integer NULL,
    tags jsonb NOT NULL DEFAULT '[]'::jsonb,
    matched_query varchar(300) NULL,
    observed_at timestamp without time zone NOT NULL,
    collected_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    raw_payload jsonb NULL,
    rag_document_id uuid NULL REFERENCES public.rag_documents(id) ON DELETE SET NULL,
    CONSTRAINT chk_market_listing_platform CHECK (char_length(btrim(platform)) > 0),
    CONSTRAINT chk_market_listing_title CHECK (char_length(btrim(title)) BETWEEN 1 AND 500),
    CONSTRAINT chk_market_listing_price CHECK (price IS NULL OR price >= 0),
    CONSTRAINT chk_market_listing_quantity CHECK (quantity IS NULL OR quantity >= 0),
    CONSTRAINT chk_market_listing_favorites CHECK (favorites IS NULL OR favorites >= 0),
    CONSTRAINT chk_market_listing_views CHECK (views IS NULL OR views >= 0),
    CONSTRAINT chk_market_listing_tags CHECK (jsonb_typeof(tags) = 'array'),
    CONSTRAINT chk_market_listing_raw_payload CHECK (raw_payload IS NULL OR jsonb_typeof(raw_payload) = 'object')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_market_listing_snapshot_identity
    ON public.market_listing_snapshots (platform, external_listing_id, observed_at);

CREATE INDEX IF NOT EXISTS idx_market_listing_snapshot_lookup
    ON public.market_listing_snapshots (platform, observed_at DESC, external_listing_id);

CREATE INDEX IF NOT EXISTS idx_market_listing_snapshot_query
    ON public.market_listing_snapshots (matched_query, observed_at DESC)
    WHERE matched_query IS NOT NULL;

-- A platform-neutral opportunity shared by sellers. Seller-specific fit is stored separately below.
CREATE TABLE IF NOT EXISTS public.market_opportunities (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    platform varchar(40) NOT NULL,
    title varchar(300) NOT NULL,
    niche varchar(200) NOT NULL,
    category varchar(160) NULL,
    summary text NULL,
    market_opportunity_score numeric(6, 2) NOT NULL,
    confidence numeric(5, 4) NOT NULL,
    score_breakdown jsonb NOT NULL DEFAULT '{}'::jsonb,
    formula_version varchar(80) NOT NULL,
    status varchar(24) NOT NULL DEFAULT 'active',
    first_observed_at timestamp without time zone NOT NULL,
    last_observed_at timestamp without time zone NOT NULL,
    created_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_market_opportunity_score CHECK (market_opportunity_score BETWEEN 0 AND 100),
    CONSTRAINT chk_market_opportunity_confidence CHECK (confidence BETWEEN 0 AND 1),
    CONSTRAINT chk_market_opportunity_breakdown CHECK (jsonb_typeof(score_breakdown) = 'object'),
    CONSTRAINT chk_market_opportunity_status CHECK (status IN ('active', 'stale', 'archived'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_market_opportunity_identity
    ON public.market_opportunities (platform, lower(btrim(niche)), lower(btrim(title)));

CREATE INDEX IF NOT EXISTS idx_market_opportunity_rank
    ON public.market_opportunities (market_opportunity_score DESC, confidence DESC)
    WHERE status = 'active';

CREATE TABLE IF NOT EXISTS public.market_opportunity_evidence (
    opportunity_id uuid NOT NULL REFERENCES public.market_opportunities(id) ON DELETE CASCADE,
    snapshot_id uuid NOT NULL REFERENCES public.market_listing_snapshots(id) ON DELETE CASCADE,
    rag_document_id uuid NULL REFERENCES public.rag_documents(id) ON DELETE SET NULL,
    evidence_role varchar(24) NOT NULL DEFAULT 'supporting',
    evidence_rank integer NOT NULL DEFAULT 1,
    created_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (opportunity_id, snapshot_id),
    CONSTRAINT chk_market_opportunity_evidence_role CHECK (evidence_role IN ('supporting', 'contradicting', 'context')),
    CONSTRAINT chk_market_opportunity_evidence_rank CHECK (evidence_rank > 0)
);

CREATE INDEX IF NOT EXISTS idx_market_opportunity_evidence_snapshot
    ON public.market_opportunity_evidence (snapshot_id);

-- Seller context used by deterministic fit ranking and by the LLM analyst input snapshot.
CREATE TABLE IF NOT EXISTS public.seller_research_profiles (
    user_id uuid PRIMARY KEY REFERENCES public.users(id) ON DELETE CASCADE,
    shop_goal text NULL,
    target_audiences jsonb NOT NULL DEFAULT '[]'::jsonb,
    preferred_categories jsonb NOT NULL DEFAULT '[]'::jsonb,
    excluded_categories jsonb NOT NULL DEFAULT '[]'::jsonb,
    preferred_keywords jsonb NOT NULL DEFAULT '[]'::jsonb,
    minimum_price numeric(14, 2) NULL,
    maximum_price numeric(14, 2) NULL,
    profile_version integer NOT NULL DEFAULT 1,
    updated_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_seller_research_arrays CHECK (
        jsonb_typeof(target_audiences) = 'array'
        AND jsonb_typeof(preferred_categories) = 'array'
        AND jsonb_typeof(excluded_categories) = 'array'
        AND jsonb_typeof(preferred_keywords) = 'array'
    ),
    CONSTRAINT chk_seller_research_price_range CHECK (
        minimum_price IS NULL OR maximum_price IS NULL OR minimum_price <= maximum_price
    )
);

CREATE TABLE IF NOT EXISTS public.seller_opportunities (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id uuid NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    opportunity_id uuid NOT NULL REFERENCES public.market_opportunities(id) ON DELETE CASCADE,
    seller_fit_score numeric(6, 2) NOT NULL,
    final_score numeric(6, 2) NOT NULL,
    rank integer NULL,
    state varchar(24) NOT NULL DEFAULT 'new',
    fit_explanation text NULL,
    fit_breakdown jsonb NOT NULL DEFAULT '{}'::jsonb,
    first_seen_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_evaluated_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    acted_at timestamp without time zone NULL,
    CONSTRAINT chk_seller_opportunity_fit_score CHECK (seller_fit_score BETWEEN 0 AND 100),
    CONSTRAINT chk_seller_opportunity_final_score CHECK (final_score BETWEEN 0 AND 100),
    CONSTRAINT chk_seller_opportunity_rank CHECK (rank IS NULL OR rank > 0),
    CONSTRAINT chk_seller_opportunity_state CHECK (state IN ('new', 'saved', 'ignored', 'acted', 'dismissed')),
    CONSTRAINT chk_seller_opportunity_breakdown CHECK (jsonb_typeof(fit_breakdown) = 'object'),
    UNIQUE (user_id, opportunity_id)
);

CREATE INDEX IF NOT EXISTS idx_seller_opportunity_rank
    ON public.seller_opportunities (user_id, final_score DESC, last_evaluated_at DESC);

CREATE INDEX IF NOT EXISTS idx_seller_opportunity_state
    ON public.seller_opportunities (user_id, state);

-- Immutable input/output audit trail for each LLM analysis attempt.
CREATE TABLE IF NOT EXISTS public.opportunity_analysis_runs (
    id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
    seller_opportunity_id uuid NOT NULL REFERENCES public.seller_opportunities(id) ON DELETE CASCADE,
    status varchar(24) NOT NULL DEFAULT 'queued',
    provider varchar(80) NULL,
    model varchar(160) NULL,
    prompt_version varchar(80) NOT NULL,
    schema_version varchar(80) NOT NULL,
    input_snapshot jsonb NOT NULL,
    output_snapshot jsonb NULL,
    error_message text NULL,
    requested_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    started_at timestamp without time zone NULL,
    completed_at timestamp without time zone NULL,
    CONSTRAINT chk_opportunity_analysis_status CHECK (status IN ('queued', 'running', 'succeeded', 'failed', 'cancelled')),
    CONSTRAINT chk_opportunity_analysis_input CHECK (jsonb_typeof(input_snapshot) = 'object'),
    CONSTRAINT chk_opportunity_analysis_output CHECK (output_snapshot IS NULL OR jsonb_typeof(output_snapshot) = 'object')
);

CREATE INDEX IF NOT EXISTS idx_opportunity_analysis_runs_status
    ON public.opportunity_analysis_runs (status, requested_at);

CREATE INDEX IF NOT EXISTS idx_opportunity_analysis_runs_seller_opportunity
    ON public.opportunity_analysis_runs (seller_opportunity_id, requested_at DESC);

COMMIT;
