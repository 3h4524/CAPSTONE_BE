--
-- PostgreSQL database dump
--

\restrict guJu3UjORp4xa79SP7Ta3O6wLPBRYNjje8hoEo6STqfoMpP1zEdT7bunVIeXsK4

-- Dumped from database version 18.6 (c021049)
-- Dumped by pg_dump version 18.6 (Debian 18.6-4~20261007.0843.g1370a7832a2.pgdg+1)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: uuid-ossp; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS "uuid-ossp" WITH SCHEMA public;


--
-- Name: EXTENSION "uuid-ossp"; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON EXTENSION "uuid-ossp" IS 'generate universally unique identifiers (UUIDs)';


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: ai_prompts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ai_prompts (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    design_template_id uuid,
    original_description text NOT NULL,
    system_prompt text NOT NULL,
    few_shot_examples jsonb,
    generated_prompt text NOT NULL,
    version_number integer DEFAULT 1 NOT NULL,
    is_approved_by_user boolean DEFAULT false,
    user_notes text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    modified_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    batch_job_product_id uuid
);


--
-- Name: api_keys; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.api_keys (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    service_provider character varying(50) NOT NULL,
    key_identifier character varying(100) NOT NULL,
    key_value_encrypted text NOT NULL,
    key_last_4 character varying(4),
    is_active boolean DEFAULT true,
    usage_count integer DEFAULT 0,
    usage_limit integer,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    last_used_at timestamp with time zone,
    expires_at timestamp with time zone,
    created_by_ip character varying(45),
    deleted_at timestamp with time zone,
    auth_type character varying(20) DEFAULT 'api_key'::character varying NOT NULL,
    environment character varying(50),
    connected_account_name character varying(200),
    last_checked_at timestamp with time zone,
    last_check_succeeded boolean
);


--
-- Name: COLUMN api_keys.last_check_succeeded; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.api_keys.last_check_succeeded IS 'NULL means no recorded connectivity result; written by the credential validation flow.';


--
-- Name: api_usage_records; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.api_usage_records (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    batch_job_id uuid,
    product_id uuid,
    provider character varying(50) NOT NULL,
    feature character varying(50) NOT NULL,
    model_name character varying(100),
    provider_request_id character varying(255),
    request_units integer DEFAULT 1 NOT NULL,
    tokens_input integer,
    tokens_output integer,
    cost_usd numeric(12,6) DEFAULT 0 NOT NULL,
    latency_ms integer,
    status character varying(50) NOT NULL,
    error_code character varying(100),
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_api_usage_feature CHECK (((feature)::text = ANY (ARRAY[('image_generation'::character varying)::text, ('mockup'::character varying)::text, ('video'::character varying)::text, ('listing'::character varying)::text, ('seo'::character varying)::text, ('integration'::character varying)::text]))),
    CONSTRAINT chk_api_usage_provider CHECK (((provider)::text = ANY (ARRAY[('leonardo'::character varying)::text, ('stable_diffusion'::character varying)::text, ('openai'::character varying)::text, ('gemini'::character varying)::text, ('printify'::character varying)::text, ('etsy'::character varying)::text, ('ffmpeg_local'::character varying)::text]))),
    CONSTRAINT chk_api_usage_status CHECK (((status)::text = ANY (ARRAY[('success'::character varying)::text, ('failed'::character varying)::text, ('timeout'::character varying)::text])))
);


--
-- Name: audit_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_logs (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    actor_user_id uuid,
    action_type character varying(100) NOT NULL,
    resource_type character varying(100) NOT NULL,
    resource_id uuid,
    old_value jsonb,
    new_value jsonb,
    ip_address inet,
    user_agent text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: auth_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.auth_tokens (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    token_type character varying(50) NOT NULL,
    token_hash character varying(255) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    used_at timestamp with time zone,
    revoked_at timestamp with time zone,
    created_by_ip character varying(45),
    user_agent text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_auth_tokens_type CHECK (((token_type)::text = ANY (ARRAY[('password_reset'::character varying)::text, ('email_verification'::character varying)::text, ('refresh'::character varying)::text])))
);


--
-- Name: batch_job_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.batch_job_logs (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    batch_job_id uuid NOT NULL,
    batch_job_product_id uuid,
    log_level character varying(50) NOT NULL,
    event_type character varying(100) NOT NULL,
    message text NOT NULL,
    details jsonb,
    duration_ms integer,
    api_usage_record_id uuid,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_batch_job_logs_level CHECK (((log_level)::text = ANY (ARRAY[('debug'::character varying)::text, ('info'::character varying)::text, ('warning'::character varying)::text, ('error'::character varying)::text, ('critical'::character varying)::text])))
);


--
-- Name: batch_job_products; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.batch_job_products (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    batch_job_id uuid NOT NULL,
    product_id uuid,
    sequence_order integer NOT NULL,
    source_row_index integer,
    raw_row_data jsonb,
    status character varying(50) DEFAULT 'pending'::character varying NOT NULL,
    current_step character varying(50),
    started_at timestamp with time zone,
    completed_at timestamp with time zone,
    duration_seconds numeric(10,2),
    error_message text,
    retry_count integer DEFAULT 0,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    batch_id uuid NOT NULL,
    custom_subject text,
    custom_art_style text,
    custom_mood_tone text,
    custom_negative_terms text,
    custom_instructions text,
    CONSTRAINT chk_batch_job_products_status CHECK (((status)::text = ANY (ARRAY[('pending'::character varying)::text, ('validating'::character varying)::text, ('queued'::character varying)::text, ('generating_image'::character varying)::text, ('image_review_required'::character varying)::text, ('generating_mockup'::character varying)::text, ('generating_video'::character varying)::text, ('generating_listing'::character varying)::text, ('review_required'::character varying)::text, ('approved'::character varying)::text, ('exported'::character varying)::text, ('published'::character varying)::text, ('failed'::character varying)::text, ('skipped'::character varying)::text, ('cancelled'::character varying)::text])))
);


--
-- Name: batch_jobs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.batch_jobs (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    source_file_type character varying(50) NOT NULL,
    source_file_url text,
    source_file_hash character varying(64),
    status character varying(50) DEFAULT 'draft'::character varying NOT NULL,
    progress_percentage numeric(5,2) DEFAULT 0,
    total_products integer DEFAULT 0,
    processed_products integer DEFAULT 0,
    failed_products integer DEFAULT 0,
    skipped_products integer DEFAULT 0,
    config jsonb DEFAULT '{}'::jsonb NOT NULL,
    priority character varying(50) DEFAULT 'normal'::character varying NOT NULL,
    estimated_cost_usd numeric(10,2) DEFAULT 0,
    actual_cost_usd numeric(12,6) DEFAULT 0,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    started_at timestamp with time zone,
    completed_at timestamp with time zone,
    estimated_completion_time timestamp with time zone,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone,
    batch_id uuid NOT NULL,
    job_type character varying(40) DEFAULT 'legacy'::character varying NOT NULL,
    CONSTRAINT chk_batch_jobs_job_type CHECK (((job_type)::text = ANY (ARRAY[('design_generation'::character varying)::text, ('video_generation'::character varying)::text, ('listing_generation'::character varying)::text, ('legacy'::character varying)::text]))),
    CONSTRAINT chk_batch_jobs_priority CHECK (((priority)::text = ANY (ARRAY[('low'::character varying)::text, ('normal'::character varying)::text, ('high'::character varying)::text]))),
    CONSTRAINT chk_batch_jobs_source_type CHECK (((source_file_type)::text = ANY (ARRAY[('csv'::character varying)::text, ('xls'::character varying)::text, ('xlsx'::character varying)::text, ('manual'::character varying)::text]))),
    CONSTRAINT chk_batch_jobs_status CHECK (((status)::text = ANY (ARRAY[('draft'::character varying)::text, ('validating'::character varying)::text, ('ready'::character varying)::text, ('queued'::character varying)::text, ('running'::character varying)::text, ('paused'::character varying)::text, ('cancelling'::character varying)::text, ('cancelled'::character varying)::text, ('partially_completed'::character varying)::text, ('completed'::character varying)::text, ('failed'::character varying)::text])))
);


--
-- Name: batches; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.batches (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    default_niche character varying(150),
    default_product_type character varying(50),
    input_method character varying(20) NOT NULL,
    status character varying(20) DEFAULT 'draft'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone,
    deleted_at timestamp with time zone,
    CONSTRAINT chk_batches_input_method CHECK (((input_method)::text = ANY (ARRAY[('file'::character varying)::text, ('manual'::character varying)::text]))),
    CONSTRAINT chk_batches_status CHECK (((status)::text = ANY (ARRAY[('draft'::character varying)::text, ('processing'::character varying)::text, ('completed'::character varying)::text])))
);


--
-- Name: design_images; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.design_images (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    ai_prompt_id uuid NOT NULL,
    batch_job_id uuid,
    api_usage_record_id uuid,
    image_generator_model character varying(100) NOT NULL,
    storage_provider character varying(50) DEFAULT 's3'::character varying NOT NULL,
    storage_key character varying(500) NOT NULL,
    image_url text NOT NULL,
    image_width_px integer NOT NULL,
    image_height_px integer NOT NULL,
    file_format character varying(10) NOT NULL,
    file_size_mb numeric(10,2) NOT NULL,
    quality_score numeric(3,2),
    user_rating integer,
    approval_status character varying(50) DEFAULT 'pending'::character varying NOT NULL,
    is_final boolean DEFAULT false,
    variation_index integer DEFAULT 1 NOT NULL,
    generation_time_seconds numeric(10,3) NOT NULL,
    generation_metadata jsonb,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone,
    batch_job_product_id uuid,
    CONSTRAINT chk_design_images_approval CHECK (((approval_status)::text = ANY (ARRAY[('pending'::character varying)::text, ('approved'::character varying)::text, ('rejected'::character varying)::text]))),
    CONSTRAINT chk_design_images_rating CHECK (((user_rating IS NULL) OR ((user_rating >= 1) AND (user_rating <= 5))))
);


--
-- Name: design_templates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.design_templates (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid,
    name character varying(255) NOT NULL,
    type character varying(50) NOT NULL,
    niche_category character varying(255),
    art_style character varying(100),
    base_prompt text NOT NULL,
    example_prompts jsonb DEFAULT '[]'::jsonb NOT NULL,
    style_description text,
    preview_image_url text,
    is_system_template boolean DEFAULT false NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    usage_count integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone,
    negative_prompt text,
    CONSTRAINT chk_design_templates_art_style CHECK (((art_style)::text = ANY ((ARRAY['vintage'::character varying, 'minimalist'::character varying, 'watercolor'::character varying, 'bold_typography'::character varying, 'dark_academia'::character varying, 'funny_quote'::character varying, 'floral'::character varying])::text[]))),
    CONSTRAINT chk_design_templates_base_prompt_length CHECK (((char_length(base_prompt) >= 1) AND (char_length(base_prompt) <= 1000))),
    CONSTRAINT chk_design_templates_example_prompts_array CHECK ((jsonb_typeof(example_prompts) = 'array'::text)),
    CONSTRAINT chk_design_templates_system_owner CHECK (((user_id IS NULL) = is_system_template)),
    CONSTRAINT chk_design_templates_type CHECK (((type)::text = ANY ((ARRAY['system'::character varying, 'personal'::character varying])::text[])))
);


--
-- Name: etsy_integrations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.etsy_integrations (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    etsy_shop_id character varying(255) NOT NULL,
    etsy_oauth_token_encrypted text NOT NULL,
    etsy_refresh_token_encrypted text,
    shop_name character varying(255) NOT NULL,
    is_default boolean DEFAULT false,
    total_listings_created integer DEFAULT 0,
    total_listings_updated integer DEFAULT 0,
    last_sync_at timestamp with time zone,
    oauth_expires_at timestamp with time zone,
    is_active boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone
);


--
-- Name: etsy_upload_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.etsy_upload_logs (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    etsy_integration_id uuid NOT NULL,
    product_id uuid NOT NULL,
    batch_job_id uuid,
    idempotency_key character varying(255) NOT NULL,
    etsy_listing_id character varying(255),
    upload_status character varying(50) NOT NULL,
    listing_state character varying(50) DEFAULT 'draft'::character varying NOT NULL,
    publish_immediately boolean DEFAULT false NOT NULL,
    confirmed_by_user_at timestamp with time zone,
    upload_payload jsonb NOT NULL,
    api_response jsonb,
    error_message text,
    retry_count integer DEFAULT 0,
    attempted_at timestamp with time zone NOT NULL,
    completed_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_etsy_listing_state CHECK (((listing_state)::text = ANY (ARRAY[('draft'::character varying)::text, ('active'::character varying)::text]))),
    CONSTRAINT chk_etsy_publish_needs_confirmation CHECK (((publish_immediately = false) OR (confirmed_by_user_at IS NOT NULL))),
    CONSTRAINT chk_etsy_upload_status CHECK (((upload_status)::text = ANY (ARRAY[('pending'::character varying)::text, ('success'::character varying)::text, ('failed'::character varying)::text, ('retrying'::character varying)::text])))
);


--
-- Name: export_package_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.export_package_items (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    export_package_id uuid NOT NULL,
    product_id uuid NOT NULL,
    include_design_images boolean DEFAULT true,
    include_mockup_images boolean DEFAULT true,
    include_promo_video boolean DEFAULT true,
    include_listing_content boolean DEFAULT true,
    folder_path_in_zip character varying(500),
    item_status character varying(50) DEFAULT 'pending'::character varying NOT NULL,
    error_message text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_export_item_status CHECK (((item_status)::text = ANY (ARRAY[('pending'::character varying)::text, ('packed'::character varying)::text, ('failed'::character varying)::text, ('skipped'::character varying)::text])))
);


--
-- Name: export_packages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.export_packages (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    batch_job_id uuid,
    user_id uuid NOT NULL,
    type character varying(50) NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    storage_provider character varying(50) DEFAULT 's3'::character varying,
    storage_key character varying(500),
    download_url text,
    file_size_mb numeric(10,2),
    creation_time_seconds numeric(10,2),
    status character varying(50) DEFAULT 'preparing'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    downloaded_at timestamp with time zone,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT chk_export_packages_status CHECK (((status)::text = ANY (ARRAY[('preparing'::character varying)::text, ('ready'::character varying)::text, ('failed'::character varying)::text, ('expired'::character varying)::text]))),
    CONSTRAINT chk_export_packages_type CHECK (((type)::text = ANY (ARRAY[('zip_package'::character varying)::text, ('listing_csv'::character varying)::text])))
);


--
-- Name: invoices; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.invoices (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    subscription_id uuid NOT NULL,
    user_id uuid NOT NULL,
    invoice_number character varying(50) NOT NULL,
    invoice_date date NOT NULL,
    due_date date NOT NULL,
    amount_usd numeric(10,2) NOT NULL,
    tax_amount numeric(10,2) DEFAULT 0 NOT NULL,
    total_amount numeric(10,2) NOT NULL,
    status character varying(50) DEFAULT 'draft'::character varying NOT NULL,
    payment_date date,
    items jsonb DEFAULT '[]'::jsonb NOT NULL,
    pdf_url text,
    stripe_invoice_id character varying(255),
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    payos_order_code bigint,
    payos_payment_link_id text,
    payos_qr_code text,
    CONSTRAINT chk_invoices_status CHECK (((status)::text = ANY (ARRAY[('draft'::character varying)::text, ('issued'::character varying)::text, ('paid'::character varying)::text, ('overdue'::character varying)::text, ('void'::character varying)::text, ('refunded'::character varying)::text]))),
    CONSTRAINT chk_invoices_total CHECK ((total_amount = (amount_usd + tax_amount)))
);


--
-- Name: listing_contents; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.listing_contents (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    batch_job_id uuid,
    api_usage_record_id uuid,
    ai_model_used character varying(100) NOT NULL,
    model_version character varying(50) NOT NULL,
    generation_time_seconds numeric(10,2) NOT NULL,
    approval_status character varying(50) DEFAULT 'pending'::character varying NOT NULL,
    approved_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    batch_job_product_id uuid,
    CONSTRAINT chk_listing_contents_approval CHECK (((approval_status)::text = ANY (ARRAY[('pending'::character varying)::text, ('approved'::character varying)::text, ('rejected'::character varying)::text]))),
    CONSTRAINT chk_listing_contents_approved_at CHECK ((((approval_status)::text <> 'approved'::text) OR (approved_at IS NOT NULL)))
);


--
-- Name: listing_descriptions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.listing_descriptions (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    listing_content_id uuid NOT NULL,
    ai_generated_description text NOT NULL,
    current_description text NOT NULL,
    is_user_edited boolean DEFAULT false NOT NULL,
    word_count integer NOT NULL,
    structure_followed jsonb DEFAULT '{}'::jsonb NOT NULL,
    keyword_density_optimal boolean NOT NULL,
    seo_score numeric(5,2) DEFAULT 0 NOT NULL,
    version_number integer DEFAULT 1 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    modified_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: listing_generation_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.listing_generation_history (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    listing_content_id uuid NOT NULL,
    api_usage_record_id uuid,
    generation_number integer NOT NULL,
    prompt_used text NOT NULL,
    adjustment_hint text,
    raw_ai_response text NOT NULL,
    title_generated character varying(140),
    tags_generated text[],
    description_generated text,
    user_action character varying(100) NOT NULL,
    feedback_notes text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_listing_history_user_action CHECK (((user_action)::text = ANY (ARRAY[('accepted'::character varying)::text, ('edited'::character varying)::text, ('regenerated'::character varying)::text, ('rejected'::character varying)::text])))
);


--
-- Name: listing_tag_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.listing_tag_items (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    listing_tag_id uuid NOT NULL,
    tag_value character varying(20) NOT NULL,
    normalized_value character varying(20) NOT NULL,
    tag_type character varying(50) NOT NULL,
    "position" integer NOT NULL,
    source character varying(20) DEFAULT 'ai'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_tag_position_range CHECK ((("position" >= 1) AND ("position" <= 13))),
    CONSTRAINT chk_tag_source CHECK (((source)::text = ANY (ARRAY[('ai'::character varying)::text, ('user'::character varying)::text]))),
    CONSTRAINT chk_tag_type CHECK (((tag_type)::text = ANY (ARRAY[('primary'::character varying)::text, ('long_tail'::character varying)::text, ('niche'::character varying)::text, ('occasion'::character varying)::text, ('product_type'::character varying)::text])))
);


--
-- Name: listing_tags; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.listing_tags (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    listing_content_id uuid NOT NULL,
    tag_count integer NOT NULL,
    tag_type_distribution jsonb DEFAULT '{}'::jsonb NOT NULL,
    seo_score numeric(5,2) DEFAULT 0 NOT NULL,
    is_user_edited boolean DEFAULT false NOT NULL,
    version_number integer DEFAULT 1 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    modified_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: listing_titles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.listing_titles (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    listing_content_id uuid NOT NULL,
    ai_generated_title character varying(140) NOT NULL,
    current_title character varying(140) NOT NULL,
    is_user_edited boolean DEFAULT false NOT NULL,
    character_count integer NOT NULL,
    includes_primary_keyword boolean NOT NULL,
    seo_score numeric(5,2) DEFAULT 0 NOT NULL,
    version_number integer DEFAULT 1 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    modified_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_listing_titles_char_count CHECK ((character_count = length((current_title)::text))),
    CONSTRAINT chk_listing_titles_edited_flag CHECK ((is_user_edited = ((current_title)::text IS DISTINCT FROM (ai_generated_title)::text)))
);


--
-- Name: mockup_images; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mockup_images (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    design_image_id uuid NOT NULL,
    product_id uuid NOT NULL,
    mockup_template_id uuid NOT NULL,
    api_usage_record_id uuid,
    storage_provider character varying(50) DEFAULT 's3'::character varying NOT NULL,
    storage_key character varying(500) NOT NULL,
    mockup_image_url text NOT NULL,
    mockup_width_px integer NOT NULL,
    mockup_height_px integer NOT NULL,
    generation_time_seconds numeric(10,2),
    approval_status character varying(50) DEFAULT 'pending'::character varying NOT NULL,
    is_final boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone,
    batch_job_product_id uuid,
    garment_color character varying(7),
    CONSTRAINT chk_mockup_images_approval CHECK (((approval_status)::text = ANY (ARRAY[('pending'::character varying)::text, ('approved'::character varying)::text, ('rejected'::character varying)::text])))
);


--
-- Name: mockup_templates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mockup_templates (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    name character varying(255) NOT NULL,
    product_type character varying(50) NOT NULL,
    base_image_url text NOT NULL,
    preview_image_url text,
    print_area_config jsonb DEFAULT '{}'::jsonb NOT NULL,
    output_width_px integer NOT NULL,
    output_height_px integer NOT NULL,
    is_system_template boolean DEFAULT true,
    is_active boolean DEFAULT true,
    usage_count integer DEFAULT 0,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    user_id uuid,
    print_maps_source_url text,
    allow_recolor boolean DEFAULT false NOT NULL,
    garment_color character varying(7),
    print_maps_version bigint,
    garment_is_light boolean DEFAULT false NOT NULL,
    background_removed boolean DEFAULT false NOT NULL,
    CONSTRAINT chk_mockup_templates_product_type CHECK (((product_type)::text = ANY (ARRAY[('tshirt'::character varying)::text, ('hoodie'::character varying)::text, ('mug'::character varying)::text, ('poster'::character varying)::text, ('tote_bag'::character varying)::text, ('phone_case'::character varying)::text])))
);


--
-- Name: music_tracks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.music_tracks (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    name character varying(255) NOT NULL,
    artist_name character varying(255) NOT NULL,
    duration_seconds integer NOT NULL,
    genre character varying(100) NOT NULL,
    mood character varying(100) NOT NULL,
    royalty_free boolean DEFAULT false NOT NULL,
    license_type character varying(100) NOT NULL,
    license_source character varying(255),
    audio_url text NOT NULL,
    preview_url text,
    is_available boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone,
    CONSTRAINT chk_music_tracks_license_source CHECK (((royalty_free = false) OR (license_source IS NOT NULL)))
);


--
-- Name: notification_alerts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.notification_alerts (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    batch_job_id uuid,
    type character varying(100) NOT NULL,
    title character varying(255) NOT NULL,
    message text NOT NULL,
    severity character varying(50) NOT NULL,
    is_read boolean DEFAULT false,
    read_at timestamp with time zone,
    action_url character varying(500),
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    expires_at timestamp with time zone,
    deleted_at timestamp with time zone,
    CONSTRAINT chk_notification_severity CHECK (((severity)::text = ANY (ARRAY[('info'::character varying)::text, ('warning'::character varying)::text, ('error'::character varying)::text, ('critical'::character varying)::text])))
);


--
-- Name: notification_deliveries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.notification_deliveries (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    notification_alert_id uuid NOT NULL,
    channel character varying(50) NOT NULL,
    delivery_status character varying(50) DEFAULT 'pending'::character varying NOT NULL,
    sent_at timestamp with time zone,
    error_message text,
    retry_count integer DEFAULT 0,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_notification_channel CHECK (((channel)::text = ANY (ARRAY[('in_app'::character varying)::text, ('email'::character varying)::text, ('webhook'::character varying)::text]))),
    CONSTRAINT chk_notification_delivery_status CHECK (((delivery_status)::text = ANY (ARRAY[('pending'::character varying)::text, ('sent'::character varying)::text, ('failed'::character varying)::text, ('skipped'::character varying)::text])))
);


--
-- Name: payment_methods; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_methods (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    payment_type character varying(50) NOT NULL,
    stripe_payment_method_id character varying(255),
    paypal_email_encrypted text,
    card_last_4_digits character varying(4),
    card_brand character varying(50),
    is_default boolean DEFAULT false,
    is_active boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone
);


--
-- Name: permissions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.permissions (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    code character varying(100) NOT NULL,
    resource character varying(100) NOT NULL,
    action character varying(50) NOT NULL,
    description text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: plan_features; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.plan_features (
    plan_id uuid NOT NULL,
    feature_code character varying(100) NOT NULL,
    is_enabled boolean DEFAULT true,
    limit_value integer,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: printify_integrations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.printify_integrations (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    printify_store_id character varying(255) NOT NULL,
    printify_api_token_encrypted text NOT NULL,
    shop_name character varying(255) NOT NULL,
    shop_title character varying(255),
    is_default boolean DEFAULT false,
    total_products_uploaded integer DEFAULT 0,
    last_sync_at timestamp with time zone,
    is_active boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone
);


--
-- Name: printify_upload_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.printify_upload_logs (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    printify_integration_id uuid NOT NULL,
    product_id uuid NOT NULL,
    batch_job_id uuid,
    idempotency_key character varying(255) NOT NULL,
    printify_product_id character varying(255),
    sync_type character varying(50) NOT NULL,
    upload_payload jsonb NOT NULL,
    api_response jsonb,
    upload_status character varying(50) NOT NULL,
    error_message text,
    retry_count integer DEFAULT 0,
    attempted_at timestamp with time zone NOT NULL,
    completed_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_printify_sync_type CHECK (((sync_type)::text = ANY (ARRAY[('create'::character varying)::text, ('update'::character varying)::text]))),
    CONSTRAINT chk_printify_upload_status CHECK (((upload_status)::text = ANY (ARRAY[('pending'::character varying)::text, ('success'::character varying)::text, ('failed'::character varying)::text, ('retrying'::character varying)::text])))
);


--
-- Name: product_mockup_templates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_mockup_templates (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    mockup_template_id uuid NOT NULL,
    sequence_order integer DEFAULT 1 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: products; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.products (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    design_template_id uuid,
    name character varying(255) NOT NULL,
    product_type character varying(50) NOT NULL,
    niche_category character varying(255),
    target_audience character varying(255),
    input_description text NOT NULL,
    desired_design_text text,
    style_preset character varying(100),
    main_keywords character varying(500),
    color_preference character varying(255),
    notes text,
    processing_status character varying(50) DEFAULT 'pending'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone,
    batch_id uuid NOT NULL,
    CONSTRAINT chk_products_status CHECK (((processing_status)::text = ANY (ARRAY[('pending'::character varying)::text, ('validating'::character varying)::text, ('queued'::character varying)::text, ('generating_image'::character varying)::text, ('image_review_required'::character varying)::text, ('generating_mockup'::character varying)::text, ('generating_video'::character varying)::text, ('generating_listing'::character varying)::text, ('review_required'::character varying)::text, ('approved'::character varying)::text, ('exported'::character varying)::text, ('published'::character varying)::text, ('failed'::character varying)::text, ('skipped'::character varying)::text, ('cancelled'::character varying)::text]))),
    CONSTRAINT chk_products_type CHECK (((product_type)::text = ANY (ARRAY[('tshirt'::character varying)::text, ('hoodie'::character varying)::text, ('mug'::character varying)::text, ('poster'::character varying)::text, ('tote_bag'::character varying)::text, ('phone_case'::character varying)::text])))
);


--
-- Name: promo_video_scenes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.promo_video_scenes (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    promo_video_id uuid NOT NULL,
    design_image_id uuid,
    mockup_image_id uuid,
    scene_order integer NOT NULL,
    duration_seconds numeric(5,2) DEFAULT 3 NOT NULL,
    transition_effect character varying(50),
    text_overlay_content text,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_scene_exactly_one_asset CHECK (((design_image_id IS NOT NULL) <> (mockup_image_id IS NOT NULL))),
    CONSTRAINT chk_scene_order_positive CHECK ((scene_order > 0)),
    CONSTRAINT chk_scene_transition CHECK (((transition_effect IS NULL) OR ((transition_effect)::text = ANY (ARRAY[('fade'::character varying)::text, ('slide'::character varying)::text, ('zoom'::character varying)::text, ('ken_burns'::character varying)::text, ('none'::character varying)::text]))))
);


--
-- Name: promo_videos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.promo_videos (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    batch_job_id uuid,
    video_template_id uuid NOT NULL,
    music_track_id uuid,
    api_usage_record_id uuid,
    text_overlay_content text,
    text_overlay_color character varying(7),
    text_overlay_font character varying(100),
    storage_provider character varying(50) DEFAULT 's3'::character varying,
    storage_key character varying(500),
    video_url text,
    video_duration_seconds integer NOT NULL,
    video_resolution character varying(50) NOT NULL,
    file_format character varying(10) NOT NULL,
    aspect_ratio character varying(20) NOT NULL,
    file_size_mb numeric(10,2),
    platform_target character varying(50) NOT NULL,
    quality_score numeric(3,2),
    user_rating integer,
    status character varying(50) DEFAULT 'pending'::character varying NOT NULL,
    approval_status character varying(50) DEFAULT 'pending'::character varying NOT NULL,
    is_final boolean DEFAULT false,
    generation_time_seconds numeric(10,2),
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone,
    batch_job_product_id uuid,
    CONSTRAINT chk_promo_videos_approval CHECK (((approval_status)::text = ANY (ARRAY[('pending'::character varying)::text, ('approved'::character varying)::text, ('rejected'::character varying)::text]))),
    CONSTRAINT chk_promo_videos_duration CHECK (((video_duration_seconds >= 15) AND (video_duration_seconds <= 30))),
    CONSTRAINT chk_promo_videos_status CHECK (((status)::text = ANY (ARRAY[('pending'::character varying)::text, ('queued'::character varying)::text, ('rendering'::character varying)::text, ('completed'::character varying)::text, ('failed'::character varying)::text])))
);


--
-- Name: role_permissions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.role_permissions (
    role_id uuid NOT NULL,
    permission_id uuid NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.roles (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    code character varying(50) NOT NULL,
    name character varying(100) NOT NULL,
    description text,
    is_system_role boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: seo_scores; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.seo_scores (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    listing_content_id uuid NOT NULL,
    overall_seo_score numeric(5,2) NOT NULL,
    title_score numeric(5,2) NOT NULL,
    tags_score numeric(5,2) NOT NULL,
    description_score numeric(5,2) NOT NULL,
    keyword_optimization_score numeric(5,2) NOT NULL,
    tag_relevance_score numeric(5,2) NOT NULL,
    keyword_density numeric(5,2) NOT NULL,
    improvement_suggestions jsonb DEFAULT '[]'::jsonb NOT NULL,
    scoring_algorithm_version character varying(20) DEFAULT 'v1'::character varying NOT NULL,
    calculated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_seo_scores_range CHECK (((overall_seo_score >= (0)::numeric) AND (overall_seo_score <= (100)::numeric)))
);


--
-- Name: share_hashtags; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.share_hashtags (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    social_media_share_id uuid NOT NULL,
    hashtag character varying(100) NOT NULL,
    "position" integer NOT NULL
);


--
-- Name: social_media_shares; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.social_media_shares (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    product_id uuid NOT NULL,
    promo_video_id uuid NOT NULL,
    platform character varying(50) NOT NULL,
    share_status character varying(50) DEFAULT 'pending'::character varying NOT NULL,
    scheduled_time timestamp with time zone,
    posted_time timestamp with time zone,
    post_caption text,
    external_post_id character varying(255),
    external_platform_url text,
    error_message text,
    retry_count integer DEFAULT 0,
    api_response jsonb,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone,
    CONSTRAINT chk_social_share_status CHECK (((share_status)::text = ANY (ARRAY[('pending'::character varying)::text, ('scheduled'::character varying)::text, ('posted'::character varying)::text, ('failed'::character varying)::text])))
);


--
-- Name: style_art_presets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.style_art_presets (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying NOT NULL,
    description text NOT NULL,
    style_modifiers text NOT NULL,
    preview_image_url text,
    recommendations jsonb DEFAULT '[]'::jsonb NOT NULL,
    is_system_template boolean DEFAULT true NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    usage_count integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    user_id uuid
);


--
-- Name: subscription_plans; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.subscription_plans (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    name character varying(100) NOT NULL,
    tier character varying(50) NOT NULL,
    description text,
    monthly_price_usd numeric(10,2) DEFAULT 0 NOT NULL,
    annual_price_usd numeric(10,2),
    max_batch_size integer DEFAULT 100 NOT NULL,
    max_products_per_month integer DEFAULT 1000 NOT NULL,
    max_concurrent_jobs integer DEFAULT 1 NOT NULL,
    image_generation_quota integer DEFAULT 500 NOT NULL,
    video_generation_quota integer DEFAULT 50 NOT NULL,
    api_call_quota integer DEFAULT 10000 NOT NULL,
    storage_quota_gb numeric(10,2) DEFAULT 100 NOT NULL,
    priority_support boolean DEFAULT false,
    custom_api_keys_allowed boolean DEFAULT false,
    is_active boolean DEFAULT true,
    sort_order integer DEFAULT 0,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: subscriptions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.subscriptions (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    plan_id uuid NOT NULL,
    billing_cycle character varying(50) NOT NULL,
    monthly_price_usd numeric(10,2) NOT NULL,
    annual_price_usd numeric(10,2),
    status character varying(50) DEFAULT 'active'::character varying NOT NULL,
    start_date date NOT NULL,
    renewal_date date NOT NULL,
    trial_ends_at timestamp with time zone,
    cancelled_at timestamp with time zone,
    auto_renew boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone,
    scheduled_plan_id uuid,
    scheduled_plan_effective_date date,
    CONSTRAINT chk_subscriptions_billing_cycle CHECK (((billing_cycle)::text = ANY (ARRAY[('monthly'::character varying)::text, ('annual'::character varying)::text]))),
    CONSTRAINT chk_subscriptions_date_order CHECK ((renewal_date >= start_date)),
    CONSTRAINT chk_subscriptions_scheduled_downgrade_pair CHECK (((scheduled_plan_id IS NULL) = (scheduled_plan_effective_date IS NULL))),
    CONSTRAINT chk_subscriptions_status CHECK (((status)::text = ANY (ARRAY[('trialing'::character varying)::text, ('active'::character varying)::text, ('past_due'::character varying)::text, ('cancelled'::character varying)::text, ('expired'::character varying)::text])))
);


--
-- Name: support_tickets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.support_tickets (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    ticket_number character varying(50) NOT NULL,
    subject character varying(255) NOT NULL,
    description text NOT NULL,
    category character varying(100) NOT NULL,
    priority character varying(50) DEFAULT 'normal'::character varying NOT NULL,
    assigned_to uuid,
    status character varying(50) DEFAULT 'open'::character varying NOT NULL,
    satisfaction_rating integer,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    resolved_at timestamp with time zone,
    CONSTRAINT chk_support_tickets_priority CHECK (((priority)::text = ANY (ARRAY[('low'::character varying)::text, ('normal'::character varying)::text, ('high'::character varying)::text, ('urgent'::character varying)::text]))),
    CONSTRAINT chk_support_tickets_rating CHECK (((satisfaction_rating IS NULL) OR ((satisfaction_rating >= 1) AND (satisfaction_rating <= 5)))),
    CONSTRAINT chk_support_tickets_status CHECK (((status)::text = ANY (ARRAY[('open'::character varying)::text, ('in_progress'::character varying)::text, ('waiting_customer'::character varying)::text, ('resolved'::character varying)::text, ('closed'::character varying)::text])))
);


--
-- Name: ticket_attachments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ticket_attachments (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    support_ticket_id uuid,
    ticket_reply_id uuid,
    file_url text NOT NULL,
    file_name character varying(255) NOT NULL,
    mime_type character varying(100) NOT NULL,
    file_size_mb numeric(10,2) NOT NULL,
    uploaded_by uuid NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_attachment_single_owner CHECK (((support_ticket_id IS NOT NULL) <> (ticket_reply_id IS NOT NULL)))
);


--
-- Name: ticket_replies; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ticket_replies (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    support_ticket_id uuid NOT NULL,
    author_id uuid NOT NULL,
    reply_text text NOT NULL,
    is_internal_note boolean DEFAULT false,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: usage_statistics; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.usage_statistics (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    billing_period_start date NOT NULL,
    billing_period_end date NOT NULL,
    images_generated integer DEFAULT 0,
    videos_created integer DEFAULT 0,
    listings_exported integer DEFAULT 0,
    total_api_calls integer DEFAULT 0,
    total_api_cost_usd numeric(12,6) DEFAULT 0,
    storage_used_gb numeric(10,2) DEFAULT 0,
    batch_jobs_completed integer DEFAULT 0,
    average_processing_time_seconds numeric(10,2) DEFAULT 0,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: user_profiles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_profiles (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid NOT NULL,
    shop_name character varying(255),
    shop_description text,
    timezone character varying(50) DEFAULT 'UTC'::character varying NOT NULL,
    language character varying(10) DEFAULT 'en'::character varying NOT NULL,
    theme_preference character varying(50) DEFAULT 'light'::character varying,
    notification_email_enabled boolean DEFAULT true,
    newsletter_subscribed boolean DEFAULT false,
    two_factor_enabled boolean DEFAULT false,
    profile_completion_percentage numeric(5,2) DEFAULT 0,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: user_roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_roles (
    user_id uuid NOT NULL,
    role_id uuid NOT NULL,
    granted_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    granted_by uuid,
    revoked_at timestamp with time zone
);


--
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.users (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    email character varying(255) NOT NULL,
    password_hash character varying(255),
    full_name character varying(255) NOT NULL,
    oauth_google_id character varying(255),
    oauth_provider character varying(50),
    avatar_url text,
    account_status character varying(50) DEFAULT 'active'::character varying NOT NULL,
    email_verified boolean DEFAULT false,
    email_verified_at timestamp with time zone,
    last_login_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamp with time zone,
    birthday timestamp with time zone,
    suspended_until timestamp with time zone,
    CONSTRAINT chk_users_account_status CHECK (((account_status)::text = ANY (ARRAY[('active'::character varying)::text, ('locked'::character varying)::text, ('suspended'::character varying)::text, ('pending_verification'::character varying)::text]))),
    CONSTRAINT chk_users_has_credential CHECK (((password_hash IS NOT NULL) OR (oauth_google_id IS NOT NULL)))
);


--
-- Name: video_templates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.video_templates (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    name character varying(255) NOT NULL,
    type character varying(100) NOT NULL,
    platform character varying(50) NOT NULL,
    duration_seconds integer NOT NULL,
    aspect_ratio character varying(20) NOT NULL,
    resolution character varying(50) NOT NULL,
    effects_config jsonb DEFAULT '{}'::jsonb NOT NULL,
    preview_video_url text,
    is_system_template boolean DEFAULT true,
    is_active boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT chk_video_templates_duration CHECK (((duration_seconds >= 15) AND (duration_seconds <= 30))),
    CONSTRAINT chk_video_templates_type CHECK (((type)::text = ANY (ARRAY[('slideshow'::character varying)::text, ('product_showcase'::character varying)::text, ('lifestyle_reel'::character varying)::text, ('story_vertical'::character varying)::text])))
);


--
-- Name: ai_prompts ai_prompts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_prompts
    ADD CONSTRAINT ai_prompts_pkey PRIMARY KEY (id);


--
-- Name: api_keys api_keys_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.api_keys
    ADD CONSTRAINT api_keys_pkey PRIMARY KEY (id);


--
-- Name: api_usage_records api_usage_records_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.api_usage_records
    ADD CONSTRAINT api_usage_records_pkey PRIMARY KEY (id);


--
-- Name: audit_logs audit_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs
    ADD CONSTRAINT audit_logs_pkey PRIMARY KEY (id);


--
-- Name: auth_tokens auth_tokens_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auth_tokens
    ADD CONSTRAINT auth_tokens_pkey PRIMARY KEY (id);


--
-- Name: auth_tokens auth_tokens_token_hash_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auth_tokens
    ADD CONSTRAINT auth_tokens_token_hash_key UNIQUE (token_hash);


--
-- Name: batch_job_logs batch_job_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_job_logs
    ADD CONSTRAINT batch_job_logs_pkey PRIMARY KEY (id);


--
-- Name: batch_job_products batch_job_products_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_job_products
    ADD CONSTRAINT batch_job_products_pkey PRIMARY KEY (id);


--
-- Name: batch_jobs batch_jobs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_jobs
    ADD CONSTRAINT batch_jobs_pkey PRIMARY KEY (id);


--
-- Name: batches batches_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batches
    ADD CONSTRAINT batches_pkey PRIMARY KEY (id);


--
-- Name: design_images design_images_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.design_images
    ADD CONSTRAINT design_images_pkey PRIMARY KEY (id);


--
-- Name: design_templates design_templates_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.design_templates
    ADD CONSTRAINT design_templates_pkey PRIMARY KEY (id);


--
-- Name: etsy_integrations etsy_integrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.etsy_integrations
    ADD CONSTRAINT etsy_integrations_pkey PRIMARY KEY (id);


--
-- Name: etsy_upload_logs etsy_upload_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.etsy_upload_logs
    ADD CONSTRAINT etsy_upload_logs_pkey PRIMARY KEY (id);


--
-- Name: export_package_items export_package_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.export_package_items
    ADD CONSTRAINT export_package_items_pkey PRIMARY KEY (id);


--
-- Name: export_packages export_packages_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.export_packages
    ADD CONSTRAINT export_packages_pkey PRIMARY KEY (id);


--
-- Name: invoices invoices_invoice_number_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_invoice_number_key UNIQUE (invoice_number);


--
-- Name: invoices invoices_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_pkey PRIMARY KEY (id);


--
-- Name: listing_contents listing_contents_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_contents
    ADD CONSTRAINT listing_contents_pkey PRIMARY KEY (id);


--
-- Name: listing_contents listing_contents_product_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_contents
    ADD CONSTRAINT listing_contents_product_id_key UNIQUE (product_id);


--
-- Name: listing_descriptions listing_descriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_descriptions
    ADD CONSTRAINT listing_descriptions_pkey PRIMARY KEY (id);


--
-- Name: listing_generation_history listing_generation_history_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_generation_history
    ADD CONSTRAINT listing_generation_history_pkey PRIMARY KEY (id);


--
-- Name: listing_tag_items listing_tag_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_tag_items
    ADD CONSTRAINT listing_tag_items_pkey PRIMARY KEY (id);


--
-- Name: listing_tags listing_tags_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_tags
    ADD CONSTRAINT listing_tags_pkey PRIMARY KEY (id);


--
-- Name: listing_titles listing_titles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_titles
    ADD CONSTRAINT listing_titles_pkey PRIMARY KEY (id);


--
-- Name: mockup_images mockup_images_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mockup_images
    ADD CONSTRAINT mockup_images_pkey PRIMARY KEY (id);


--
-- Name: mockup_templates mockup_templates_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mockup_templates
    ADD CONSTRAINT mockup_templates_pkey PRIMARY KEY (id);


--
-- Name: music_tracks music_tracks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.music_tracks
    ADD CONSTRAINT music_tracks_pkey PRIMARY KEY (id);


--
-- Name: notification_alerts notification_alerts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_alerts
    ADD CONSTRAINT notification_alerts_pkey PRIMARY KEY (id);


--
-- Name: notification_deliveries notification_deliveries_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_deliveries
    ADD CONSTRAINT notification_deliveries_pkey PRIMARY KEY (id);


--
-- Name: payment_methods payment_methods_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_methods
    ADD CONSTRAINT payment_methods_pkey PRIMARY KEY (id);


--
-- Name: permissions permissions_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.permissions
    ADD CONSTRAINT permissions_code_key UNIQUE (code);


--
-- Name: permissions permissions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.permissions
    ADD CONSTRAINT permissions_pkey PRIMARY KEY (id);


--
-- Name: plan_features plan_features_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.plan_features
    ADD CONSTRAINT plan_features_pkey PRIMARY KEY (plan_id, feature_code);


--
-- Name: printify_integrations printify_integrations_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.printify_integrations
    ADD CONSTRAINT printify_integrations_pkey PRIMARY KEY (id);


--
-- Name: printify_upload_logs printify_upload_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.printify_upload_logs
    ADD CONSTRAINT printify_upload_logs_pkey PRIMARY KEY (id);


--
-- Name: product_mockup_templates product_mockup_templates_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_mockup_templates
    ADD CONSTRAINT product_mockup_templates_pkey PRIMARY KEY (id);


--
-- Name: products products_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_pkey PRIMARY KEY (id);


--
-- Name: promo_video_scenes promo_video_scenes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promo_video_scenes
    ADD CONSTRAINT promo_video_scenes_pkey PRIMARY KEY (id);


--
-- Name: promo_videos promo_videos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promo_videos
    ADD CONSTRAINT promo_videos_pkey PRIMARY KEY (id);


--
-- Name: role_permissions role_permissions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_pkey PRIMARY KEY (role_id, permission_id);


--
-- Name: roles roles_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_code_key UNIQUE (code);


--
-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);


--
-- Name: seo_scores seo_scores_listing_content_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.seo_scores
    ADD CONSTRAINT seo_scores_listing_content_id_key UNIQUE (listing_content_id);


--
-- Name: seo_scores seo_scores_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.seo_scores
    ADD CONSTRAINT seo_scores_pkey PRIMARY KEY (id);


--
-- Name: share_hashtags share_hashtags_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.share_hashtags
    ADD CONSTRAINT share_hashtags_pkey PRIMARY KEY (id);


--
-- Name: social_media_shares social_media_shares_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.social_media_shares
    ADD CONSTRAINT social_media_shares_pkey PRIMARY KEY (id);


--
-- Name: style_art_presets style_art_presets_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.style_art_presets
    ADD CONSTRAINT style_art_presets_pkey PRIMARY KEY (id);


--
-- Name: subscription_plans subscription_plans_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_plans
    ADD CONSTRAINT subscription_plans_pkey PRIMARY KEY (id);


--
-- Name: subscription_plans subscription_plans_tier_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscription_plans
    ADD CONSTRAINT subscription_plans_tier_key UNIQUE (tier);


--
-- Name: subscriptions subscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_pkey PRIMARY KEY (id);


--
-- Name: support_tickets support_tickets_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.support_tickets
    ADD CONSTRAINT support_tickets_pkey PRIMARY KEY (id);


--
-- Name: support_tickets support_tickets_ticket_number_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.support_tickets
    ADD CONSTRAINT support_tickets_ticket_number_key UNIQUE (ticket_number);


--
-- Name: ticket_attachments ticket_attachments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_attachments
    ADD CONSTRAINT ticket_attachments_pkey PRIMARY KEY (id);


--
-- Name: ticket_replies ticket_replies_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_replies
    ADD CONSTRAINT ticket_replies_pkey PRIMARY KEY (id);


--
-- Name: batches uq_batches_id_user; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batches
    ADD CONSTRAINT uq_batches_id_user UNIQUE (id, user_id);


--
-- Name: usage_statistics usage_statistics_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usage_statistics
    ADD CONSTRAINT usage_statistics_pkey PRIMARY KEY (id);


--
-- Name: user_profiles user_profiles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_profiles
    ADD CONSTRAINT user_profiles_pkey PRIMARY KEY (id);


--
-- Name: user_profiles user_profiles_user_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_profiles
    ADD CONSTRAINT user_profiles_user_id_key UNIQUE (user_id);


--
-- Name: user_roles user_roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT user_roles_pkey PRIMARY KEY (user_id, role_id);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- Name: video_templates video_templates_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.video_templates
    ADD CONSTRAINT video_templates_pkey PRIMARY KEY (id);


--
-- Name: idx_ai_prompts_job_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_ai_prompts_job_product ON public.ai_prompts USING btree (batch_job_product_id);


--
-- Name: idx_ai_prompts_product_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_ai_prompts_product_id ON public.ai_prompts USING btree (product_id);


--
-- Name: idx_api_keys_service_provider; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_api_keys_service_provider ON public.api_keys USING btree (service_provider);


--
-- Name: idx_api_keys_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_api_keys_user_id ON public.api_keys USING btree (user_id);


--
-- Name: idx_api_usage_batch_job_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_api_usage_batch_job_id ON public.api_usage_records USING btree (batch_job_id);


--
-- Name: idx_api_usage_provider_feature; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_api_usage_provider_feature ON public.api_usage_records USING btree (provider, feature);


--
-- Name: idx_api_usage_provider_request_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_api_usage_provider_request_id ON public.api_usage_records USING btree (provider_request_id);


--
-- Name: idx_api_usage_user_created; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_api_usage_user_created ON public.api_usage_records USING btree (user_id, created_at);


--
-- Name: idx_audit_logs_actor_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_audit_logs_actor_user_id ON public.audit_logs USING btree (actor_user_id);


--
-- Name: idx_audit_logs_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_audit_logs_created_at ON public.audit_logs USING btree (created_at);


--
-- Name: idx_audit_logs_resource; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_audit_logs_resource ON public.audit_logs USING btree (resource_type, resource_id);


--
-- Name: idx_auth_tokens_expires_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_auth_tokens_expires_at ON public.auth_tokens USING btree (expires_at);


--
-- Name: idx_auth_tokens_user_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_auth_tokens_user_type ON public.auth_tokens USING btree (user_id, token_type);


--
-- Name: idx_batch_job_logs_batch_job_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_job_logs_batch_job_id ON public.batch_job_logs USING btree (batch_job_id);


--
-- Name: idx_batch_job_logs_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_job_logs_created_at ON public.batch_job_logs USING btree (created_at);


--
-- Name: idx_batch_job_logs_log_level; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_job_logs_log_level ON public.batch_job_logs USING btree (log_level);


--
-- Name: idx_batch_job_products_batch; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_job_products_batch ON public.batch_job_products USING btree (batch_id);


--
-- Name: idx_batch_job_products_batch_job_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_job_products_batch_job_id ON public.batch_job_products USING btree (batch_job_id);


--
-- Name: idx_batch_job_products_batch_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_job_products_batch_status ON public.batch_job_products USING btree (batch_job_id, status);


--
-- Name: idx_batch_job_products_product_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_job_products_product_id ON public.batch_job_products USING btree (product_id);


--
-- Name: idx_batch_job_products_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_job_products_status ON public.batch_job_products USING btree (status);


--
-- Name: idx_batch_jobs_batch_created; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_jobs_batch_created ON public.batch_jobs USING btree (batch_id, created_at DESC);


--
-- Name: idx_batch_jobs_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_jobs_created_at ON public.batch_jobs USING btree (created_at);


--
-- Name: idx_batch_jobs_started_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_jobs_started_at ON public.batch_jobs USING btree (started_at);


--
-- Name: idx_batch_jobs_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_jobs_status ON public.batch_jobs USING btree (status);


--
-- Name: idx_batch_jobs_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_jobs_user_id ON public.batch_jobs USING btree (user_id);


--
-- Name: idx_batch_jobs_user_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batch_jobs_user_status ON public.batch_jobs USING btree (user_id, status);


--
-- Name: idx_batches_user_created; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batches_user_created ON public.batches USING btree (user_id, created_at DESC);


--
-- Name: idx_design_images_approval_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_design_images_approval_status ON public.design_images USING btree (approval_status);


--
-- Name: idx_design_images_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_design_images_created_at ON public.design_images USING btree (created_at);


--
-- Name: idx_design_images_is_final; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_design_images_is_final ON public.design_images USING btree (is_final);


--
-- Name: idx_design_images_job_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_design_images_job_product ON public.design_images USING btree (batch_job_product_id);


--
-- Name: idx_design_images_product_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_design_images_product_id ON public.design_images USING btree (product_id);


--
-- Name: idx_design_templates_art_style; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_design_templates_art_style ON public.design_templates USING btree (art_style);


--
-- Name: idx_design_templates_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_design_templates_type ON public.design_templates USING btree (type);


--
-- Name: idx_design_templates_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_design_templates_user_id ON public.design_templates USING btree (user_id);


--
-- Name: idx_etsy_integrations_shop_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_etsy_integrations_shop_id ON public.etsy_integrations USING btree (etsy_shop_id);


--
-- Name: idx_etsy_integrations_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_etsy_integrations_user_id ON public.etsy_integrations USING btree (user_id);


--
-- Name: idx_etsy_upload_logs_integration_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_etsy_upload_logs_integration_id ON public.etsy_upload_logs USING btree (etsy_integration_id);


--
-- Name: idx_etsy_upload_logs_product_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_etsy_upload_logs_product_id ON public.etsy_upload_logs USING btree (product_id);


--
-- Name: idx_etsy_upload_logs_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_etsy_upload_logs_status ON public.etsy_upload_logs USING btree (upload_status);


--
-- Name: idx_export_package_items_product_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_export_package_items_product_id ON public.export_package_items USING btree (product_id);


--
-- Name: idx_export_package_items_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_export_package_items_status ON public.export_package_items USING btree (item_status);


--
-- Name: idx_export_packages_batch_job_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_export_packages_batch_job_id ON public.export_packages USING btree (batch_job_id);


--
-- Name: idx_export_packages_expires_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_export_packages_expires_at ON public.export_packages USING btree (expires_at);


--
-- Name: idx_export_packages_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_export_packages_status ON public.export_packages USING btree (status);


--
-- Name: idx_export_packages_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_export_packages_user_id ON public.export_packages USING btree (user_id);


--
-- Name: idx_invoices_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_invoices_created_at ON public.invoices USING btree (created_at);


--
-- Name: idx_invoices_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_invoices_status ON public.invoices USING btree (status);


--
-- Name: idx_invoices_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_invoices_user_id ON public.invoices USING btree (user_id);


--
-- Name: idx_listing_contents_approval_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_listing_contents_approval_status ON public.listing_contents USING btree (approval_status);


--
-- Name: idx_listing_contents_job_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_listing_contents_job_product ON public.listing_contents USING btree (batch_job_product_id);


--
-- Name: idx_listing_contents_product_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_listing_contents_product_id ON public.listing_contents USING btree (product_id);


--
-- Name: idx_listing_tag_items_normalized; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_listing_tag_items_normalized ON public.listing_tag_items USING btree (normalized_value);


--
-- Name: idx_mockup_images_design_image_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_mockup_images_design_image_id ON public.mockup_images USING btree (design_image_id);


--
-- Name: idx_mockup_images_job_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_mockup_images_job_product ON public.mockup_images USING btree (batch_job_product_id);


--
-- Name: idx_mockup_images_product_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_mockup_images_product_id ON public.mockup_images USING btree (product_id);


--
-- Name: idx_mockup_images_template_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_mockup_images_template_id ON public.mockup_images USING btree (mockup_template_id);


--
-- Name: idx_mockup_templates_is_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_mockup_templates_is_active ON public.mockup_templates USING btree (is_active);


--
-- Name: idx_mockup_templates_product_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_mockup_templates_product_type ON public.mockup_templates USING btree (product_type);


--
-- Name: idx_mockup_templates_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_mockup_templates_user_id ON public.mockup_templates USING btree (user_id);


--
-- Name: idx_music_tracks_genre; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_music_tracks_genre ON public.music_tracks USING btree (genre);


--
-- Name: idx_music_tracks_mood; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_music_tracks_mood ON public.music_tracks USING btree (mood);


--
-- Name: idx_notification_alerts_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_notification_alerts_created_at ON public.notification_alerts USING btree (created_at);


--
-- Name: idx_notification_alerts_is_read; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_notification_alerts_is_read ON public.notification_alerts USING btree (is_read);


--
-- Name: idx_notification_alerts_severity; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_notification_alerts_severity ON public.notification_alerts USING btree (severity);


--
-- Name: idx_notification_alerts_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_notification_alerts_user_id ON public.notification_alerts USING btree (user_id);


--
-- Name: idx_notification_deliveries_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_notification_deliveries_status ON public.notification_deliveries USING btree (delivery_status);


--
-- Name: idx_payment_methods_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_payment_methods_user_id ON public.payment_methods USING btree (user_id);


--
-- Name: idx_permissions_resource_action; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_permissions_resource_action ON public.permissions USING btree (resource, action);


--
-- Name: idx_printify_integrations_store_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_printify_integrations_store_id ON public.printify_integrations USING btree (printify_store_id);


--
-- Name: idx_printify_integrations_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_printify_integrations_user_id ON public.printify_integrations USING btree (user_id);


--
-- Name: idx_printify_upload_logs_integration_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_printify_upload_logs_integration_id ON public.printify_upload_logs USING btree (printify_integration_id);


--
-- Name: idx_printify_upload_logs_product_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_printify_upload_logs_product_id ON public.printify_upload_logs USING btree (product_id);


--
-- Name: idx_printify_upload_logs_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_printify_upload_logs_status ON public.printify_upload_logs USING btree (upload_status);


--
-- Name: idx_product_mockup_templates_template_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_product_mockup_templates_template_id ON public.product_mockup_templates USING btree (mockup_template_id);


--
-- Name: idx_products_batch_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_batch_status ON public.products USING btree (batch_id, processing_status);


--
-- Name: idx_products_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_created_at ON public.products USING btree (created_at);


--
-- Name: idx_products_design_template_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_design_template_id ON public.products USING btree (design_template_id);


--
-- Name: idx_products_processing_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_processing_status ON public.products USING btree (processing_status);


--
-- Name: idx_products_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_user_id ON public.products USING btree (user_id);


--
-- Name: idx_promo_video_scenes_design_image_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promo_video_scenes_design_image_id ON public.promo_video_scenes USING btree (design_image_id);


--
-- Name: idx_promo_video_scenes_mockup_image_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promo_video_scenes_mockup_image_id ON public.promo_video_scenes USING btree (mockup_image_id);


--
-- Name: idx_promo_videos_approval_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promo_videos_approval_status ON public.promo_videos USING btree (approval_status);


--
-- Name: idx_promo_videos_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promo_videos_created_at ON public.promo_videos USING btree (created_at);


--
-- Name: idx_promo_videos_job_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promo_videos_job_product ON public.promo_videos USING btree (batch_job_product_id);


--
-- Name: idx_promo_videos_product_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promo_videos_product_id ON public.promo_videos USING btree (product_id);


--
-- Name: idx_promo_videos_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_promo_videos_status ON public.promo_videos USING btree (status);


--
-- Name: idx_role_permissions_permission_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_role_permissions_permission_id ON public.role_permissions USING btree (permission_id);


--
-- Name: idx_roles_code; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_roles_code ON public.roles USING btree (code);


--
-- Name: idx_seo_scores_overall; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_seo_scores_overall ON public.seo_scores USING btree (overall_seo_score);


--
-- Name: idx_social_media_shares_platform; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_social_media_shares_platform ON public.social_media_shares USING btree (platform);


--
-- Name: idx_social_media_shares_product_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_social_media_shares_product_id ON public.social_media_shares USING btree (product_id);


--
-- Name: idx_social_media_shares_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_social_media_shares_status ON public.social_media_shares USING btree (share_status);


--
-- Name: idx_style_art_presets_is_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_style_art_presets_is_active ON public.style_art_presets USING btree (is_active);


--
-- Name: idx_style_art_presets_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_style_art_presets_user_id ON public.style_art_presets USING btree (user_id);


--
-- Name: idx_subscription_plans_is_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_subscription_plans_is_active ON public.subscription_plans USING btree (is_active);


--
-- Name: idx_subscription_plans_tier; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_subscription_plans_tier ON public.subscription_plans USING btree (tier);


--
-- Name: idx_subscriptions_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_subscriptions_created_at ON public.subscriptions USING btree (created_at);


--
-- Name: idx_subscriptions_renewal_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_subscriptions_renewal_date ON public.subscriptions USING btree (renewal_date);


--
-- Name: idx_subscriptions_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_subscriptions_status ON public.subscriptions USING btree (status);


--
-- Name: idx_subscriptions_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_subscriptions_user_id ON public.subscriptions USING btree (user_id);


--
-- Name: idx_support_tickets_assigned_to; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_support_tickets_assigned_to ON public.support_tickets USING btree (assigned_to);


--
-- Name: idx_support_tickets_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_support_tickets_created_at ON public.support_tickets USING btree (created_at);


--
-- Name: idx_support_tickets_priority; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_support_tickets_priority ON public.support_tickets USING btree (priority);


--
-- Name: idx_support_tickets_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_support_tickets_status ON public.support_tickets USING btree (status);


--
-- Name: idx_support_tickets_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_support_tickets_user_id ON public.support_tickets USING btree (user_id);


--
-- Name: idx_ticket_attachments_reply_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_ticket_attachments_reply_id ON public.ticket_attachments USING btree (ticket_reply_id);


--
-- Name: idx_ticket_attachments_ticket_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_ticket_attachments_ticket_id ON public.ticket_attachments USING btree (support_ticket_id);


--
-- Name: idx_ticket_replies_author_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_ticket_replies_author_id ON public.ticket_replies USING btree (author_id);


--
-- Name: idx_ticket_replies_support_ticket_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_ticket_replies_support_ticket_id ON public.ticket_replies USING btree (support_ticket_id);


--
-- Name: idx_usage_statistics_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_usage_statistics_user_id ON public.usage_statistics USING btree (user_id);


--
-- Name: idx_user_profiles_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_user_profiles_user_id ON public.user_profiles USING btree (user_id);


--
-- Name: idx_user_roles_role_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_user_roles_role_id ON public.user_roles USING btree (role_id);


--
-- Name: idx_users_account_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_users_account_status ON public.users USING btree (account_status);


--
-- Name: idx_users_deleted_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_users_deleted_at ON public.users USING btree (deleted_at);


--
-- Name: idx_users_email; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_users_email ON public.users USING btree (email);


--
-- Name: idx_users_oauth_google_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_users_oauth_google_id ON public.users USING btree (oauth_google_id);


--
-- Name: idx_video_templates_platform; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_video_templates_platform ON public.video_templates USING btree (platform);


--
-- Name: idx_video_templates_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_video_templates_type ON public.video_templates USING btree (type);


--
-- Name: uq_ai_prompts_product_version; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_ai_prompts_product_version ON public.ai_prompts USING btree (product_id, version_number);


--
-- Name: uq_batch_job_product_order; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_batch_job_product_order ON public.batch_job_products USING btree (batch_job_id, sequence_order);


--
-- Name: uq_batch_job_products_id_product; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_batch_job_products_id_product ON public.batch_job_products USING btree (id, product_id);


--
-- Name: uq_batch_job_products_job_product; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_batch_job_products_job_product ON public.batch_job_products USING btree (batch_job_id, product_id) WHERE (product_id IS NOT NULL);


--
-- Name: uq_batch_jobs_id_batch; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_batch_jobs_id_batch ON public.batch_jobs USING btree (id, batch_id);


--
-- Name: uq_batches_user_name_active; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_batches_user_name_active ON public.batches USING btree (user_id, lower((name)::text)) WHERE (deleted_at IS NULL);


--
-- Name: uq_etsy_idempotency; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_etsy_idempotency ON public.etsy_upload_logs USING btree (etsy_integration_id, idempotency_key);


--
-- Name: uq_export_package_product; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_export_package_product ON public.export_package_items USING btree (export_package_id, product_id);


--
-- Name: uq_listing_descriptions_version; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_listing_descriptions_version ON public.listing_descriptions USING btree (listing_content_id, version_number);


--
-- Name: uq_listing_generation_number; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_listing_generation_number ON public.listing_generation_history USING btree (listing_content_id, generation_number);


--
-- Name: uq_listing_tag_no_duplicate; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_listing_tag_no_duplicate ON public.listing_tag_items USING btree (listing_tag_id, normalized_value);


--
-- Name: uq_listing_tag_position; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_listing_tag_position ON public.listing_tag_items USING btree (listing_tag_id, "position");


--
-- Name: uq_listing_tags_version; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_listing_tags_version ON public.listing_tags USING btree (listing_content_id, version_number);


--
-- Name: uq_listing_titles_version; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_listing_titles_version ON public.listing_titles USING btree (listing_content_id, version_number);


--
-- Name: uq_notification_delivery_channel; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_notification_delivery_channel ON public.notification_deliveries USING btree (notification_alert_id, channel);


--
-- Name: uq_printify_idempotency; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_printify_idempotency ON public.printify_upload_logs USING btree (printify_integration_id, idempotency_key);


--
-- Name: uq_product_mockup_template; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_product_mockup_template ON public.product_mockup_templates USING btree (product_id, mockup_template_id);


--
-- Name: uq_products_batch_id_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_products_batch_id_id ON public.products USING btree (batch_id, id);


--
-- Name: uq_products_batch_name_active; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_products_batch_name_active ON public.products USING btree (batch_id, lower((name)::text)) WHERE (deleted_at IS NULL);


--
-- Name: uq_promo_video_scene_order; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_promo_video_scene_order ON public.promo_video_scenes USING btree (promo_video_id, scene_order);


--
-- Name: uq_share_hashtag_position; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_share_hashtag_position ON public.share_hashtags USING btree (social_media_share_id, "position");


--
-- Name: uq_usage_statistics_period; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_usage_statistics_period ON public.usage_statistics USING btree (user_id, billing_period_start, billing_period_end);


--
-- Name: ux_design_templates_personal_name_active; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_design_templates_personal_name_active ON public.design_templates USING btree (user_id, lower(btrim((name)::text))) WHERE ((deleted_at IS NULL) AND (is_system_template = false));


--
-- Name: ux_mockup_templates_name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_mockup_templates_name ON public.mockup_templates USING btree (lower((name)::text));


--
-- Name: ux_style_art_presets_name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_style_art_presets_name ON public.style_art_presets USING btree (lower((name)::text));


--
-- Name: ai_prompts ai_prompts_design_template_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_prompts
    ADD CONSTRAINT ai_prompts_design_template_id_fkey FOREIGN KEY (design_template_id) REFERENCES public.design_templates(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: ai_prompts ai_prompts_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_prompts
    ADD CONSTRAINT ai_prompts_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: api_keys api_keys_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.api_keys
    ADD CONSTRAINT api_keys_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: api_usage_records api_usage_records_batch_job_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.api_usage_records
    ADD CONSTRAINT api_usage_records_batch_job_id_fkey FOREIGN KEY (batch_job_id) REFERENCES public.batch_jobs(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: api_usage_records api_usage_records_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.api_usage_records
    ADD CONSTRAINT api_usage_records_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: api_usage_records api_usage_records_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.api_usage_records
    ADD CONSTRAINT api_usage_records_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: audit_logs audit_logs_actor_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs
    ADD CONSTRAINT audit_logs_actor_user_id_fkey FOREIGN KEY (actor_user_id) REFERENCES public.users(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: auth_tokens auth_tokens_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auth_tokens
    ADD CONSTRAINT auth_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: batch_job_logs batch_job_logs_api_usage_record_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_job_logs
    ADD CONSTRAINT batch_job_logs_api_usage_record_id_fkey FOREIGN KEY (api_usage_record_id) REFERENCES public.api_usage_records(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: batch_job_logs batch_job_logs_batch_job_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_job_logs
    ADD CONSTRAINT batch_job_logs_batch_job_id_fkey FOREIGN KEY (batch_job_id) REFERENCES public.batch_jobs(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: batch_job_logs batch_job_logs_batch_job_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_job_logs
    ADD CONSTRAINT batch_job_logs_batch_job_product_id_fkey FOREIGN KEY (batch_job_product_id) REFERENCES public.batch_job_products(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: batch_jobs batch_jobs_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_jobs
    ADD CONSTRAINT batch_jobs_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: batches batches_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batches
    ADD CONSTRAINT batches_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: design_images design_images_ai_prompt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.design_images
    ADD CONSTRAINT design_images_ai_prompt_id_fkey FOREIGN KEY (ai_prompt_id) REFERENCES public.ai_prompts(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: design_images design_images_api_usage_record_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.design_images
    ADD CONSTRAINT design_images_api_usage_record_id_fkey FOREIGN KEY (api_usage_record_id) REFERENCES public.api_usage_records(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: design_images design_images_batch_job_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.design_images
    ADD CONSTRAINT design_images_batch_job_id_fkey FOREIGN KEY (batch_job_id) REFERENCES public.batch_jobs(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: design_images design_images_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.design_images
    ADD CONSTRAINT design_images_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: design_templates design_templates_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.design_templates
    ADD CONSTRAINT design_templates_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: etsy_integrations etsy_integrations_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.etsy_integrations
    ADD CONSTRAINT etsy_integrations_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: etsy_upload_logs etsy_upload_logs_batch_job_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.etsy_upload_logs
    ADD CONSTRAINT etsy_upload_logs_batch_job_id_fkey FOREIGN KEY (batch_job_id) REFERENCES public.batch_jobs(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: etsy_upload_logs etsy_upload_logs_etsy_integration_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.etsy_upload_logs
    ADD CONSTRAINT etsy_upload_logs_etsy_integration_id_fkey FOREIGN KEY (etsy_integration_id) REFERENCES public.etsy_integrations(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: etsy_upload_logs etsy_upload_logs_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.etsy_upload_logs
    ADD CONSTRAINT etsy_upload_logs_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: export_package_items export_package_items_export_package_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.export_package_items
    ADD CONSTRAINT export_package_items_export_package_id_fkey FOREIGN KEY (export_package_id) REFERENCES public.export_packages(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: export_package_items export_package_items_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.export_package_items
    ADD CONSTRAINT export_package_items_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: export_packages export_packages_batch_job_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.export_packages
    ADD CONSTRAINT export_packages_batch_job_id_fkey FOREIGN KEY (batch_job_id) REFERENCES public.batch_jobs(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: export_packages export_packages_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.export_packages
    ADD CONSTRAINT export_packages_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: ai_prompts fk_ai_prompts_job_product; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ai_prompts
    ADD CONSTRAINT fk_ai_prompts_job_product FOREIGN KEY (batch_job_product_id) REFERENCES public.batch_job_products(id) ON DELETE SET NULL;


--
-- Name: batch_job_products fk_batch_job_products_job_batch; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_job_products
    ADD CONSTRAINT fk_batch_job_products_job_batch FOREIGN KEY (batch_job_id, batch_id) REFERENCES public.batch_jobs(id, batch_id) ON DELETE CASCADE;


--
-- Name: batch_job_products fk_batch_job_products_product_batch; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_job_products
    ADD CONSTRAINT fk_batch_job_products_product_batch FOREIGN KEY (batch_id, product_id) REFERENCES public.products(batch_id, id);


--
-- Name: batch_jobs fk_batch_jobs_batch_owner; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batch_jobs
    ADD CONSTRAINT fk_batch_jobs_batch_owner FOREIGN KEY (batch_id, user_id) REFERENCES public.batches(id, user_id) ON DELETE RESTRICT;


--
-- Name: design_images fk_design_images_job_product; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.design_images
    ADD CONSTRAINT fk_design_images_job_product FOREIGN KEY (batch_job_product_id) REFERENCES public.batch_job_products(id) ON DELETE SET NULL;


--
-- Name: listing_contents fk_listing_contents_job_product; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_contents
    ADD CONSTRAINT fk_listing_contents_job_product FOREIGN KEY (batch_job_product_id) REFERENCES public.batch_job_products(id) ON DELETE SET NULL;


--
-- Name: mockup_images fk_mockup_images_job_product; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mockup_images
    ADD CONSTRAINT fk_mockup_images_job_product FOREIGN KEY (batch_job_product_id) REFERENCES public.batch_job_products(id) ON DELETE SET NULL;


--
-- Name: products fk_products_batch_owner; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT fk_products_batch_owner FOREIGN KEY (batch_id, user_id) REFERENCES public.batches(id, user_id) ON DELETE RESTRICT;


--
-- Name: promo_videos fk_promo_videos_job_product; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promo_videos
    ADD CONSTRAINT fk_promo_videos_job_product FOREIGN KEY (batch_job_product_id) REFERENCES public.batch_job_products(id) ON DELETE SET NULL;


--
-- Name: invoices invoices_subscription_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_subscription_id_fkey FOREIGN KEY (subscription_id) REFERENCES public.subscriptions(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: invoices invoices_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: listing_contents listing_contents_api_usage_record_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_contents
    ADD CONSTRAINT listing_contents_api_usage_record_id_fkey FOREIGN KEY (api_usage_record_id) REFERENCES public.api_usage_records(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: listing_contents listing_contents_batch_job_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_contents
    ADD CONSTRAINT listing_contents_batch_job_id_fkey FOREIGN KEY (batch_job_id) REFERENCES public.batch_jobs(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: listing_contents listing_contents_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_contents
    ADD CONSTRAINT listing_contents_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: listing_descriptions listing_descriptions_listing_content_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_descriptions
    ADD CONSTRAINT listing_descriptions_listing_content_id_fkey FOREIGN KEY (listing_content_id) REFERENCES public.listing_contents(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: listing_generation_history listing_generation_history_api_usage_record_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_generation_history
    ADD CONSTRAINT listing_generation_history_api_usage_record_id_fkey FOREIGN KEY (api_usage_record_id) REFERENCES public.api_usage_records(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: listing_generation_history listing_generation_history_listing_content_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_generation_history
    ADD CONSTRAINT listing_generation_history_listing_content_id_fkey FOREIGN KEY (listing_content_id) REFERENCES public.listing_contents(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: listing_tag_items listing_tag_items_listing_tag_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_tag_items
    ADD CONSTRAINT listing_tag_items_listing_tag_id_fkey FOREIGN KEY (listing_tag_id) REFERENCES public.listing_tags(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: listing_tags listing_tags_listing_content_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_tags
    ADD CONSTRAINT listing_tags_listing_content_id_fkey FOREIGN KEY (listing_content_id) REFERENCES public.listing_contents(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: listing_titles listing_titles_listing_content_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.listing_titles
    ADD CONSTRAINT listing_titles_listing_content_id_fkey FOREIGN KEY (listing_content_id) REFERENCES public.listing_contents(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: mockup_images mockup_images_api_usage_record_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mockup_images
    ADD CONSTRAINT mockup_images_api_usage_record_id_fkey FOREIGN KEY (api_usage_record_id) REFERENCES public.api_usage_records(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: mockup_images mockup_images_design_image_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mockup_images
    ADD CONSTRAINT mockup_images_design_image_id_fkey FOREIGN KEY (design_image_id) REFERENCES public.design_images(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: mockup_images mockup_images_mockup_template_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mockup_images
    ADD CONSTRAINT mockup_images_mockup_template_id_fkey FOREIGN KEY (mockup_template_id) REFERENCES public.mockup_templates(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: mockup_images mockup_images_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mockup_images
    ADD CONSTRAINT mockup_images_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: mockup_templates mockup_templates_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mockup_templates
    ADD CONSTRAINT mockup_templates_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: notification_alerts notification_alerts_batch_job_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_alerts
    ADD CONSTRAINT notification_alerts_batch_job_id_fkey FOREIGN KEY (batch_job_id) REFERENCES public.batch_jobs(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: notification_alerts notification_alerts_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_alerts
    ADD CONSTRAINT notification_alerts_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: notification_deliveries notification_deliveries_notification_alert_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_deliveries
    ADD CONSTRAINT notification_deliveries_notification_alert_id_fkey FOREIGN KEY (notification_alert_id) REFERENCES public.notification_alerts(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: payment_methods payment_methods_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_methods
    ADD CONSTRAINT payment_methods_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: plan_features plan_features_plan_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.plan_features
    ADD CONSTRAINT plan_features_plan_id_fkey FOREIGN KEY (plan_id) REFERENCES public.subscription_plans(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: printify_integrations printify_integrations_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.printify_integrations
    ADD CONSTRAINT printify_integrations_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: printify_upload_logs printify_upload_logs_batch_job_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.printify_upload_logs
    ADD CONSTRAINT printify_upload_logs_batch_job_id_fkey FOREIGN KEY (batch_job_id) REFERENCES public.batch_jobs(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: printify_upload_logs printify_upload_logs_printify_integration_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.printify_upload_logs
    ADD CONSTRAINT printify_upload_logs_printify_integration_id_fkey FOREIGN KEY (printify_integration_id) REFERENCES public.printify_integrations(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: printify_upload_logs printify_upload_logs_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.printify_upload_logs
    ADD CONSTRAINT printify_upload_logs_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: product_mockup_templates product_mockup_templates_mockup_template_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_mockup_templates
    ADD CONSTRAINT product_mockup_templates_mockup_template_id_fkey FOREIGN KEY (mockup_template_id) REFERENCES public.mockup_templates(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: product_mockup_templates product_mockup_templates_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_mockup_templates
    ADD CONSTRAINT product_mockup_templates_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: products products_design_template_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_design_template_id_fkey FOREIGN KEY (design_template_id) REFERENCES public.design_templates(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: products products_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: promo_video_scenes promo_video_scenes_design_image_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promo_video_scenes
    ADD CONSTRAINT promo_video_scenes_design_image_id_fkey FOREIGN KEY (design_image_id) REFERENCES public.design_images(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: promo_video_scenes promo_video_scenes_mockup_image_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promo_video_scenes
    ADD CONSTRAINT promo_video_scenes_mockup_image_id_fkey FOREIGN KEY (mockup_image_id) REFERENCES public.mockup_images(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: promo_video_scenes promo_video_scenes_promo_video_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promo_video_scenes
    ADD CONSTRAINT promo_video_scenes_promo_video_id_fkey FOREIGN KEY (promo_video_id) REFERENCES public.promo_videos(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: promo_videos promo_videos_api_usage_record_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promo_videos
    ADD CONSTRAINT promo_videos_api_usage_record_id_fkey FOREIGN KEY (api_usage_record_id) REFERENCES public.api_usage_records(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: promo_videos promo_videos_batch_job_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promo_videos
    ADD CONSTRAINT promo_videos_batch_job_id_fkey FOREIGN KEY (batch_job_id) REFERENCES public.batch_jobs(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: promo_videos promo_videos_music_track_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promo_videos
    ADD CONSTRAINT promo_videos_music_track_id_fkey FOREIGN KEY (music_track_id) REFERENCES public.music_tracks(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: promo_videos promo_videos_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promo_videos
    ADD CONSTRAINT promo_videos_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: promo_videos promo_videos_video_template_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promo_videos
    ADD CONSTRAINT promo_videos_video_template_id_fkey FOREIGN KEY (video_template_id) REFERENCES public.video_templates(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: role_permissions role_permissions_permission_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_permission_id_fkey FOREIGN KEY (permission_id) REFERENCES public.permissions(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: role_permissions role_permissions_role_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT role_permissions_role_id_fkey FOREIGN KEY (role_id) REFERENCES public.roles(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: seo_scores seo_scores_listing_content_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.seo_scores
    ADD CONSTRAINT seo_scores_listing_content_id_fkey FOREIGN KEY (listing_content_id) REFERENCES public.listing_contents(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: share_hashtags share_hashtags_social_media_share_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.share_hashtags
    ADD CONSTRAINT share_hashtags_social_media_share_id_fkey FOREIGN KEY (social_media_share_id) REFERENCES public.social_media_shares(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: social_media_shares social_media_shares_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.social_media_shares
    ADD CONSTRAINT social_media_shares_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: social_media_shares social_media_shares_promo_video_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.social_media_shares
    ADD CONSTRAINT social_media_shares_promo_video_id_fkey FOREIGN KEY (promo_video_id) REFERENCES public.promo_videos(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: style_art_presets style_art_presets_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.style_art_presets
    ADD CONSTRAINT style_art_presets_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: subscriptions subscriptions_plan_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_plan_id_fkey FOREIGN KEY (plan_id) REFERENCES public.subscription_plans(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: subscriptions subscriptions_scheduled_plan_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_scheduled_plan_id_fkey FOREIGN KEY (scheduled_plan_id) REFERENCES public.subscription_plans(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: subscriptions subscriptions_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: support_tickets support_tickets_assigned_to_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.support_tickets
    ADD CONSTRAINT support_tickets_assigned_to_fkey FOREIGN KEY (assigned_to) REFERENCES public.users(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: support_tickets support_tickets_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.support_tickets
    ADD CONSTRAINT support_tickets_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: ticket_attachments ticket_attachments_support_ticket_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_attachments
    ADD CONSTRAINT ticket_attachments_support_ticket_id_fkey FOREIGN KEY (support_ticket_id) REFERENCES public.support_tickets(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: ticket_attachments ticket_attachments_ticket_reply_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_attachments
    ADD CONSTRAINT ticket_attachments_ticket_reply_id_fkey FOREIGN KEY (ticket_reply_id) REFERENCES public.ticket_replies(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: ticket_attachments ticket_attachments_uploaded_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_attachments
    ADD CONSTRAINT ticket_attachments_uploaded_by_fkey FOREIGN KEY (uploaded_by) REFERENCES public.users(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: ticket_replies ticket_replies_author_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_replies
    ADD CONSTRAINT ticket_replies_author_id_fkey FOREIGN KEY (author_id) REFERENCES public.users(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: ticket_replies ticket_replies_support_ticket_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ticket_replies
    ADD CONSTRAINT ticket_replies_support_ticket_id_fkey FOREIGN KEY (support_ticket_id) REFERENCES public.support_tickets(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: usage_statistics usage_statistics_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usage_statistics
    ADD CONSTRAINT usage_statistics_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: user_profiles user_profiles_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_profiles
    ADD CONSTRAINT user_profiles_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- Name: user_roles user_roles_granted_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT user_roles_granted_by_fkey FOREIGN KEY (granted_by) REFERENCES public.users(id) ON DELETE SET NULL DEFERRABLE;


--
-- Name: user_roles user_roles_role_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT user_roles_role_id_fkey FOREIGN KEY (role_id) REFERENCES public.roles(id) ON DELETE RESTRICT DEFERRABLE;


--
-- Name: user_roles user_roles_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT user_roles_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE DEFERRABLE;


--
-- PostgreSQL database dump complete
--

\unrestrict guJu3UjORp4xa79SP7Ta3O6wLPBRYNjje8hoEo6STqfoMpP1zEdT7bunVIeXsK4

