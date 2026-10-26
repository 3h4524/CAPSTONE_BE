-- Apply once: lets a product carry its own image prompt, which replaces the design template for that product.
BEGIN;
ALTER TABLE public.products ADD COLUMN IF NOT EXISTS custom_prompt text;
ALTER TABLE public.products DROP CONSTRAINT IF EXISTS chk_products_custom_prompt_length;
ALTER TABLE public.products ADD CONSTRAINT chk_products_custom_prompt_length
    CHECK (custom_prompt IS NULL OR char_length(custom_prompt) <= 1000);
COMMIT;
