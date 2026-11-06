-- Synthetic apparel dataset for the Etsy clothing demo.
-- Generates 60 opportunities x 8 listings x 3 observation periods.
-- Synthetic rows are tracked by is_synthetic = true and a dedicated formula version.

BEGIN;

DELETE FROM public.market_opportunity_evidence
WHERE opportunity_id IN (
    SELECT id FROM public.market_opportunities WHERE formula_version = 'synthetic-apparel-v1'
);

DELETE FROM public.market_listing_snapshots
WHERE external_listing_id LIKE 'synthetic-etsy-apparel-%';

DELETE FROM public.market_opportunities
WHERE formula_version = 'synthetic-apparel-v1';

DO $$
DECLARE
    niches text[] := ARRAY[
        'personalized t shirt', 'graphic t shirt', 'embroidered sweatshirt',
        'custom hoodie', 'oversized sweatshirt', 'vintage shirt',
        'funny teacher shirt', 'mom shirt', 'dad shirt', 'dog mom shirt',
        'cat mom shirt', 'wedding party shirts', 'bridesmaid shirt',
        'bachelorette shirt', 'personalized couple sweatshirt',
        'family reunion shirts', 'matching family shirts', 'custom name shirt',
        'birth flower shirt', 'zodiac shirt', 'book lover shirt',
        'plant lover shirt', 'gardener shirt', 'cottagecore shirt',
        'gothic shirt', 'minimalist typography shirt', 'funny quote shirt',
        'christmas sweatshirt', 'halloween sweatshirt', 'christmas family shirt',
        'teacher sweatshirt', 'nurse sweatshirt', 'nurse shirt',
        'bridal sweatshirt', 'honeymoon shirt', 'pet memorial sweatshirt',
        'custom pet portrait shirt', 'embroidered tote bag',
        'personalized tote bag', 'canvas tote bag', 'embroidered hat',
        'custom baseball cap', 'trucker hat', 'personalized beanie',
        'crochet cardigan', 'knit sweater', 'patchwork jacket',
        'denim jacket patch', 'embroidered jacket', 'linen apron',
        'personalized apron', 'baby bodysuit', 'baby name sweatshirt',
        'kids birthday shirt', 'toddler shirt', 'matching couple shirts',
        'custom sports jersey', 'team sweatshirt', 'tie dye shirt',
        'screen print shirt'
    ];
    title_variants text[] := ARRAY[
        'Gift Set', 'Personalized Collection', 'Handmade Edition',
        'Custom Design', 'Everyday Wear', 'Gift Ready'
    ];
    listing_variants text[] := ARRAY[
        'Classic', 'Personalized', 'Minimalist', 'Handcrafted',
        'Gift Ready', 'Modern', 'Custom', 'Limited Edition'
    ];
    categories text[] := ARRAY['apparel', 'clothing', 'personalized gifts', 'accessories'];
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
        opportunity_id := uuid_generate_v5(uuid_ns_url(), format('apcs:synthetic:apparel:opportunity:%s', opportunity_index));
        score := 66 + ((opportunity_index * 9) % 31);

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
                'dataset', 'apparel-demo',
                'mode', 'hybrid',
                'formulaVersion', 'synthetic-apparel-v1'
            ),
            'synthetic-apparel-v1', 'active',
            observed_at - interval '14 days', observed_at,
            observed_at, observed_at, true
        );

        FOR listing_index IN 1..8 LOOP
            listing_id := format('synthetic-etsy-apparel-%s-%s', opportunity_index, listing_index);
            FOR period_index IN 0..2 LOOP
                snapshot_id := uuid_generate_v5(uuid_ns_url(), format('apcs:synthetic:apparel:snapshot:%s:%s:%s', opportunity_index, listing_index, period_index));
                price := round((22 + (opportunity_index % 12) + listing_index * 1.5 + period_index * 0.75)::numeric, 2);

                INSERT INTO public.market_listing_snapshots (
                    id, platform, external_listing_id, external_seller_id,
                    title, description, source_url, price, currency, quantity,
                    favorites, views, tags, matched_query, observed_at,
                    collected_at, raw_payload, rag_document_id, is_synthetic
                ) VALUES (
                    snapshot_id, 'etsy', listing_id,
                    format('synthetic-apparel-seller-%s', 1 + ((opportunity_index + listing_index) % 18)),
                    format('%s %s', title, listing_variants[listing_index]),
                    format('Handmade product reference for shoppers interested in %s.', niche),
                    format('https://www.etsy.com/listing/%s', listing_id),
                    price, 'USD', 8 + listing_index,
                    120 + opportunity_index * 5 + listing_index * 13 + period_index * 22,
                    900 + opportunity_index * 42 + listing_index * 135 + period_index * 280,
                    jsonb_build_array(niche, 'handmade', 'clothing', 'gift'),
                    niche,
                    observed_at - ((2 - period_index) * interval '7 days'),
                    observed_at - ((2 - period_index) * interval '7 days'),
                    jsonb_build_object(
                        'provenance', 'synthetic',
                        'dataset', 'apparel-demo',
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
