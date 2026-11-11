-- A system mock-up template linked to the Printify product (blueprint) its photo shows. A workflow that pushes to
-- Printify offers only linked templates, and Printify makes the linked blueprint, so the listing's mock-ups show the
-- garment Printify ships. The provider and sizes are defaults the seller may change on Apply mock-up.
CREATE TABLE IF NOT EXISTS public.mockup_template_printify_links (
    mockup_template_id uuid PRIMARY KEY REFERENCES public.mockup_templates(id) ON DELETE CASCADE,
    blueprint_id bigint NOT NULL CHECK (blueprint_id > 0),
    blueprint_title text NOT NULL,
    blueprint_image_url text NULL,
    print_provider_id bigint NULL CHECK (print_provider_id > 0),
    print_provider_title text NULL,
    default_sizes text[] NOT NULL DEFAULT '{}',
    updated_by uuid NULL REFERENCES public.users(id) ON DELETE SET NULL,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_mockup_template_printify_links_blueprint
    ON public.mockup_template_printify_links (blueprint_id);
