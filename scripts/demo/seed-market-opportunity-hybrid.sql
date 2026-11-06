-- Synthetic market dataset for Hybrid mode demos.
--
-- Generates 48 opportunities x 8 listings x 3 observation periods:
--   48 opportunities
--   1,152 listing snapshots
--   1,152 opportunity-evidence rows
--
-- Every synthetic row is identifiable by its synthetic-* external id and by
-- raw_payload.provenance = synthetic. It is safe to rerun: only this dataset
-- is removed before reseeding. Do not run against production unless the
-- database is explicitly being used for a demo.

BEGIN;

DELETE FROM public.market_opportunity_evidence
WHERE opportunity_id IN (
    SELECT id
    FROM public.market_opportunities
    WHERE title LIKE 'Synthetic Demo — %'
);

DELETE FROM public.market_listing_snapshots
WHERE external_listing_id LIKE 'synthetic-etsy-market-%';

DELETE FROM public.market_opportunities
WHERE title LIKE 'Synthetic Demo — %';

DO $$
DECLARE
    niches text[] := ARRAY[
        'personalized wedding gift', 'plant lovers gift', 'ceramic mug gift',
        'handmade candle', 'crochet bag', 'canvas tote bag',
        'embroidered sweatshirt', 'knit sweater pattern', 'quilted wall hanging',
        'macrame plant hanger', 'ceramic vase', 'terracotta planter',
        'botanical art print', 'abstract wall art', 'minimalist wall print',
        'wedding invitation template', 'bridal hair accessory', 'baby name sign',
        'nursery wall decor', 'baby shower gift', 'teacher appreciation gift',
        'graduation gift', 'housewarming gift', 'new home ornament',
        'kitchen organization', 'recipe card template', 'meal planner printable',
        'custom pet portrait', 'dog bandana', 'cat toy',
        'pet memorial jewelry', 'garden marker', 'herb garden kit',
        'terrarium kit', 'crystal jewelry', 'beaded earrings',
        'charm bracelet', 'leather wallet', 'personalized keychain',
        'phone case', 'laptop sleeve', 'crochet amigurumi',
        'cross stitch pattern', 'embroidery kit', 'watercolor tutorial',
        'scrapbooking kit', 'journaling stickers', 'reading journal'
    ];
    categories text[] := ARRAY[
        'gifts', 'home decor', 'weddings', 'accessories', 'craft supplies', 'paper goods'
    ];
    title_variants text[] := ARRAY[
        'Gift Set', 'Handmade Collection', 'Personalized Edition',
        'Starter Kit', 'Home Collection', 'Keepsake Gift'
    ];
    listing_variants text[] := ARRAY[
        'Classic', 'Personalized', 'Minimalist', 'Handcrafted',
        'Gift Ready', 'Modern', 'Custom', 'Limited Edition'
    ];
    opportunity_id uuid;
    snapshot_id uuid;
    opportunity_index int;
    listing_index int;
    period_index int;
    score numeric;
    price numeric;
    observed_at timestamp := date_trunc('day', now() at time zone 'utc');
    niche text;
    title text;
    listing_id text;
BEGIN
    FOR opportunity_index IN 1..cardinality(niches) LOOP
        niche := niches[opportunity_index];
        title := format('%s %s', initcap(niche), title_variants[1 + ((opportunity_index - 1) % cardinality(title_variants))]);
        opportunity_id := uuid_generate_v5(uuid_ns_url(), format('apcs:synthetic:opportunity:%s', opportunity_index));
        score := 68 + ((opportunity_index * 7) % 29);

        INSERT INTO public.market_opportunities (
            id, platform, title, niche, category, summary,
            market_opportunity_score, confidence, score_breakdown,
            formula_version, status, first_observed_at, last_observed_at,
            created_at, updated_at, is_synthetic
        ) VALUES (
            opportunity_id, 'etsy', title, niche,
            categories[1 + ((opportunity_index - 1) % cardinality(categories))],
            format('Curated market opportunity for shoppers interested in %s.', niche),
            score,
            0.30,
            jsonb_build_object(
                'provenance', 'synthetic',
                'mode', 'hybrid',
                'syntheticWeight', 0.30,
                'formulaVersion', 'synthetic-demo-v1'
            ),
            'synthetic-demo-v1', 'active',
            observed_at - interval '14 days', observed_at,
            observed_at, observed_at, true
        );

        FOR listing_index IN 1..8 LOOP
            listing_id := format('synthetic-etsy-market-%s-%s', opportunity_index, listing_index);
            FOR period_index IN 0..2 LOOP
                snapshot_id := uuid_generate_v5(uuid_ns_url(), format('apcs:synthetic:snapshot:%s:%s:%s', opportunity_index, listing_index, period_index));
                price := round((18 + opportunity_index + listing_index * 2 + period_index * 0.75)::numeric, 2);

                INSERT INTO public.market_listing_snapshots (
                    id, platform, external_listing_id, external_seller_id,
                    title, description, source_url, price, currency, quantity,
                    favorites, views, tags, matched_query, observed_at,
                    collected_at, raw_payload, rag_document_id, is_synthetic
                ) VALUES (
                    snapshot_id, 'etsy', listing_id,
                    format('synthetic-seller-%s', 1 + ((opportunity_index + listing_index) % 12)),
                    format('%s %s', title, listing_variants[listing_index]),
                    format('Handmade product reference for shoppers interested in %s.', niche),
                    format('https://www.etsy.com/listing/%s', listing_id),
                    price, 'USD', 10 + listing_index,
                    90 + opportunity_index * 4 + listing_index * 11 + period_index * 18,
                    700 + opportunity_index * 35 + listing_index * 120 + period_index * 260,
                    jsonb_build_array(niche, 'handmade', 'gift'),
                    niche,
                    observed_at - ((2 - period_index) * interval '7 days'),
                    observed_at - ((2 - period_index) * interval '7 days'),
                    jsonb_build_object(
                        'provenance', 'synthetic',
                        'dataset', 'market-opportunity-hybrid-demo',
                        'source', 'demo-generator',
                        'replaceWhenRealEvidenceArrives', true
                    ),
                    NULL, true
                );

                INSERT INTO public.market_opportunity_evidence (
                    opportunity_id, snapshot_id, rag_document_id,
                    evidence_role, evidence_rank, created_at
                ) VALUES (
                    opportunity_id, snapshot_id, NULL, 'context', listing_index,
                    observed_at - ((2 - period_index) * interval '7 days')
                );
            END LOOP;
        END LOOP;
    END LOOP;
END $$;

COMMIT;

