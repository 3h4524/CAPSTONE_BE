-- Quick test data: one new batch with two t-shirt products, ready to run through the workflow
-- (designs -> mock-ups -> several videos, one per design).
--
-- Usage (owner defaults to nguyeenkhang15112004@gmail.com; pass -v owner_email=... to change it):
--   psql -h localhost -U postgres -d apcs -v ON_ERROR_STOP=1 -f scripts/demo/seed-two-tshirt-batch.sql
--
-- Safe to run again: a batch with the same name for the same owner is left as it is.

\if :{?owner_email}
\else
\set owner_email 'nguyeenkhang15112004@gmail.com'
\endif

BEGIN;

-- psql does not expand variables inside $$ quotes, so hand the value over as a transaction setting.
SELECT set_config('seed.owner_email', :'owner_email', true);

DO $$
DECLARE
    v_email   text := current_setting('seed.owner_email');
    v_user    uuid;
    v_batch   uuid;
    v_name    constant text := 'Video Test - 2 T-Shirts';
BEGIN
    SELECT id INTO v_user FROM public.users WHERE lower(email) = lower(v_email) AND deleted_at IS NULL;
    IF v_user IS NULL THEN
        RAISE EXCEPTION 'No active user with email %', v_email;
    END IF;

    SELECT id INTO v_batch FROM public.batches
    WHERE user_id = v_user AND lower(name) = lower(v_name) AND deleted_at IS NULL;
    IF v_batch IS NOT NULL THEN
        RAISE NOTICE 'Batch "%" already exists (%): nothing to do.', v_name, v_batch;
        RETURN;
    END IF;

    INSERT INTO public.batches (user_id, name, description, default_niche, default_product_type, input_method, status)
    VALUES (v_user, v_name, 'Two t-shirts for testing videos of several products at once.',
            'Outdoor and lifestyle', 'tshirt', 'manual', 'draft')
    RETURNING id INTO v_batch;

    INSERT INTO public.products
        (user_id, batch_id, name, product_type, niche_category, target_audience, input_description,
         desired_design_text, style_preset, main_keywords, color_preference, notes, processing_status)
    VALUES
        (v_user, v_batch, 'Retro Mountain Sunset T-Shirt', 'tshirt',
         'Hiking and outdoor adventure lovers', 'Adults who love hiking and camping',
         'Retro mountain range at sunset with a bold vintage sun and the words "Take A Hike". '
         || 'Warm orange, red and cream colors, distressed print look, centered chest graphic.',
         'TAKE A HIKE', 'Vintage',
         'hiking shirt, mountain tee, retro sunset, outdoor gift, camping shirt, adventure tee, nature lover, vintage mountains',
         'Orange, cream, deep red', 'Test product 1 of 2.', 'pending'),
        (v_user, v_batch, 'Cozy Coffee Cat T-Shirt', 'tshirt',
         'Cat and coffee lovers', 'Cat owners and coffee drinkers',
         'A cute round cat hugging a big cup of coffee with steam hearts and the words "But First, Coffee". '
         || 'Soft pastel colors, clean flat illustration, centered chest graphic.',
         'BUT FIRST, COFFEE', 'Minimalist',
         'cat shirt, coffee tee, cat lover gift, coffee lover, cute cat, funny cat shirt, cat mom, caffeine',
         'Pastel brown, cream, soft pink', 'Test product 2 of 2.', 'pending');

    RAISE NOTICE 'Created batch "%" (%) with 2 t-shirt products.', v_name, v_batch;
END
$$;

COMMIT;

SELECT b.id AS batch_id, b.name AS batch, p.id AS product_id, p.name AS product, p.product_type, p.processing_status
FROM public.batches b
JOIN public.products p ON p.batch_id = b.id AND p.deleted_at IS NULL
WHERE b.name = 'Video Test - 2 T-Shirts' AND b.deleted_at IS NULL
  AND b.user_id = (SELECT id FROM public.users WHERE lower(email) = lower(:'owner_email'))
ORDER BY p.name;
