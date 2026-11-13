-- One market listing snapshot per listing, search query and UTC day. The collector refreshes the day's row when it
-- collects a listing again, but a worker running older code against the same database could still add a second row
-- and count the listing twice in that day's figures; the database now refuses it.
--
-- Earlier duplicates go first: the evidence of each extra row moves to the day's latest snapshot of that listing and
-- query (the one the collector keeps), then the extra rows are removed (their evidence rows cascade).

CREATE TEMP TABLE market_snapshot_duplicates AS
SELECT id, keep_id
FROM (
    SELECT id,
           first_value(id) OVER (
               PARTITION BY platform,
                            external_listing_id,
                            lower(regexp_replace(btrim(coalesce(matched_query, '')), '\s+', ' ', 'g')),
                            (observed_at)::date
               ORDER BY collected_at DESC, id) AS keep_id
    FROM public.market_listing_snapshots
) ranked
WHERE id <> keep_id;

INSERT INTO public.market_opportunity_evidence (opportunity_id, snapshot_id, rag_document_id, evidence_role, evidence_rank, created_at)
SELECT e.opportunity_id, d.keep_id, e.rag_document_id, e.evidence_role, e.evidence_rank, e.created_at
FROM public.market_opportunity_evidence e
JOIN market_snapshot_duplicates d ON d.id = e.snapshot_id
ON CONFLICT DO NOTHING;

DELETE FROM public.market_listing_snapshots s
USING market_snapshot_duplicates d
WHERE s.id = d.id;

DROP TABLE market_snapshot_duplicates;

-- The query is compared as MarketQueryKey.Normalize does: trimmed, single spaces, lower case.
CREATE UNIQUE INDEX IF NOT EXISTS uq_market_listing_snapshot_listing_query_day
    ON public.market_listing_snapshots (
        platform,
        external_listing_id,
        lower(regexp_replace(btrim(coalesce(matched_query, '')), '\s+', ' ', 'g')),
        ((observed_at)::date));
