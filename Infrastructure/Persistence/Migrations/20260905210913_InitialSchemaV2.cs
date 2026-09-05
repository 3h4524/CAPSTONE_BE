using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace APCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchemaV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mockup_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    product_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    base_image_url = table.Column<string>(type: "text", nullable: false),
                    preview_image_url = table.Column<string>(type: "text", nullable: true),
                    print_area_config = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    output_width_px = table.Column<int>(type: "integer", nullable: false),
                    output_height_px = table.Column<int>(type: "integer", nullable: false),
                    is_system_template = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mockup_templates", x => x.id);
                    table.CheckConstraint("chk_mockup_templates_product_type", "product_type IN ('tshirt', 'hoodie', 'mug', 'poster', 'tote_bag', 'phone_case')");
                });

            migrationBuilder.CreateTable(
                name: "music_tracks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    artist_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    genre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mood = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    royalty_free = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    license_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    license_source = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    audio_url = table.Column<string>(type: "text", nullable: false),
                    preview_url = table.Column<string>(type: "text", nullable: true),
                    is_available = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_music_tracks", x => x.id);
                    table.CheckConstraint("chk_music_tracks_license_source", "royalty_free = false OR license_source IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_system_role = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    normalized_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    concurrency_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    monthly_price_usd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    annual_price_usd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    max_batch_size = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    max_products_per_month = table.Column<int>(type: "integer", nullable: false, defaultValue: 1000),
                    max_concurrent_jobs = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    image_generation_quota = table.Column<int>(type: "integer", nullable: false, defaultValue: 500),
                    video_generation_quota = table.Column<int>(type: "integer", nullable: false, defaultValue: 50),
                    api_call_quota = table.Column<int>(type: "integer", nullable: false, defaultValue: 10000),
                    storage_quota_gb = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 100m),
                    priority_support = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    custom_api_keys_allowed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_plans", x => x.id);
                    table.CheckConstraint("chk_subscription_plans_annual_price", "annual_price_usd IS NULL OR annual_price_usd >= 0");
                    table.CheckConstraint("chk_subscription_plans_monthly_price", "monthly_price_usd >= 0");
                    table.CheckConstraint("chk_subscription_plans_quotas", "max_batch_size >= 0 AND max_products_per_month >= 0 AND max_concurrent_jobs >= 0 AND image_generation_quota >= 0 AND video_generation_quota >= 0 AND api_call_quota >= 0 AND storage_quota_gb >= 0");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    oauth_google_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    oauth_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    account_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "active"),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    normalized_user_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("chk_users_account_status", "account_status IN ('active', 'locked', 'suspended', 'pending_verification')");
                    table.CheckConstraint("chk_users_has_credential", "password_hash IS NOT NULL OR oauth_google_id IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "video_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    aspect_ratio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolution = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    effects_config = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    preview_video_url = table.Column<string>(type: "text", nullable: true),
                    is_system_template = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_video_templates", x => x.id);
                    table.CheckConstraint("chk_video_templates_duration", "duration_seconds BETWEEN 15 AND 30");
                    table.CheckConstraint("chk_video_templates_type", "type IN ('slideshow', 'product_showcase', 'lifestyle_reel', 'story_vertical')");
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_features",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    limit_value = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_features", x => new { x.plan_id, x.feature_code });
                    table.ForeignKey(
                        name: "fk_plan_features_subscription_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    key_identifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    key_value_encrypted = table.Column<string>(type: "text", nullable: false),
                    key_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    usage_limit = table.Column<int>(type: "integer", nullable: true),
                    last_used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_keys_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_logs_asp_net_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "auth_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    jwt_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason_revoked = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    replaced_by_token_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    revoked_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_tokens", x => x.id);
                    table.CheckConstraint("chk_auth_tokens_refresh_jwt_id", "token_type <> 'refresh' OR jwt_id IS NOT NULL");
                    table.CheckConstraint("chk_auth_tokens_revoked_reason", "revoked_at IS NULL OR reason_revoked IS NOT NULL");
                    table.CheckConstraint("chk_auth_tokens_type", "token_type IN ('password_reset', 'email_verification', 'refresh')");
                    table.ForeignKey(
                        name: "fk_auth_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "batch_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    source_file_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_file_url = table.Column<string>(type: "text", nullable: true),
                    source_file_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "draft"),
                    progress_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    total_products = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    processed_products = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    failed_products = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    skipped_products = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    config = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "normal"),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    actual_cost_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false, defaultValue: 0m),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estimated_completion_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_jobs", x => x.id);
                    table.CheckConstraint("chk_batch_jobs_priority", "priority IN ('low', 'normal', 'high')");
                    table.CheckConstraint("chk_batch_jobs_source_type", "source_file_type IN ('csv', 'xlsx', 'manual')");
                    table.CheckConstraint("chk_batch_jobs_status", "status IN ('draft', 'validating', 'ready', 'queued', 'running', 'paused', 'cancelling', 'cancelled', 'partially_completed', 'completed', 'failed')");
                    table.ForeignKey(
                        name: "fk_batch_jobs_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "design_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    niche_category = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    art_style = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    base_prompt = table.Column<string>(type: "text", nullable: false),
                    example_prompts = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    style_description = table.Column<string>(type: "text", nullable: true),
                    preview_image_url = table.Column<string>(type: "text", nullable: true),
                    is_system_template = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_templates", x => x.id);
                    table.CheckConstraint("chk_design_templates_art_style", "art_style IN ('vintage', 'minimalist', 'watercolor', 'bold_typography', 'dark_academia', 'funny_quote', 'floral')");
                    table.CheckConstraint("chk_design_templates_system_owner", "(user_id IS NULL) = is_system_template");
                    table.ForeignKey(
                        name: "fk_design_templates_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "etsy_integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    etsy_shop_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    etsy_oauth_token_encrypted = table.Column<string>(type: "text", nullable: false),
                    etsy_refresh_token_encrypted = table.Column<string>(type: "text", nullable: true),
                    shop_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    total_listings_created = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_listings_updated = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    oauth_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_etsy_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_etsy_integrations_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    stripe_payment_method_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    paypal_email_encrypted = table.Column<string>(type: "text", nullable: true),
                    card_last4digits = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    card_brand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_methods", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_methods_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "printify_integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    printify_store_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    printify_api_token_encrypted = table.Column<string>(type: "text", nullable: false),
                    shop_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    shop_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    total_products_uploaded = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_printify_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_printify_integrations_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    billing_cycle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    monthly_price_usd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    annual_price_usd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "active"),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    renewal_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trial_ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    auto_renew = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.CheckConstraint("chk_subscriptions_billing_cycle", "billing_cycle IN ('monthly', 'annual')");
                    table.CheckConstraint("chk_subscriptions_date_order", "renewal_date >= start_date");
                    table.CheckConstraint("chk_subscriptions_status", "status IN ('trialing', 'active', 'past_due', 'cancelled', 'expired')");
                    table.ForeignKey(
                        name: "fk_subscriptions_subscription_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "normal"),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "open"),
                    satisfaction_rating = table.Column<int>(type: "integer", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_tickets", x => x.id);
                    table.CheckConstraint("chk_support_tickets_priority", "priority IN ('low', 'normal', 'high', 'urgent')");
                    table.CheckConstraint("chk_support_tickets_rating", "satisfaction_rating IS NULL OR satisfaction_rating BETWEEN 1 AND 5");
                    table.CheckConstraint("chk_support_tickets_status", "status IN ('open', 'in_progress', 'waiting_customer', 'resolved', 'closed')");
                    table.ForeignKey(
                        name: "fk_support_tickets_asp_net_users_assigned_to",
                        column: x => x.assigned_to,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_support_tickets_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usage_statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    billing_period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    billing_period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    images_generated = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    videos_created = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    listings_exported = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_api_calls = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_api_cost_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false, defaultValue: 0m),
                    storage_used_gb = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    batch_jobs_completed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    average_processing_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_statistics", x => x.id);
                    table.ForeignKey(
                        name: "fk_usage_statistics_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    shop_description = table.Column<string>(type: "text", nullable: true),
                    timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "UTC"),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "en"),
                    theme_preference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "light"),
                    notification_email_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    newsletter_subscribed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    profile_completion_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_role_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_role_history", x => x.id);
                    table.CheckConstraint("chk_user_role_history_action", "action IN ('granted', 'revoked')");
                    table.ForeignKey(
                        name: "fk_user_role_history_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_role_history_users_performed_by",
                        column: x => x.performed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_user_role_history_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_granted_by",
                        column: x => x.granted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "export_packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    storage_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: "s3"),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    download_url = table.Column<string>(type: "text", nullable: true),
                    file_size_mb = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    creation_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "preparing"),
                    downloaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_packages", x => x.id);
                    table.CheckConstraint("chk_export_packages_status", "status IN ('preparing', 'ready', 'failed', 'expired')");
                    table.CheckConstraint("chk_export_packages_type", "type IN ('zip_package', 'listing_csv')");
                    table.ForeignKey(
                        name: "fk_export_packages_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_export_packages_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notification_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    action_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_alerts", x => x.id);
                    table.CheckConstraint("chk_notification_severity", "severity IN ('info', 'warning', 'error', 'critical')");
                    table.ForeignKey(
                        name: "fk_notification_alerts_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notification_alerts_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    design_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    product_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    niche_category = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    target_audience = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    input_description = table.Column<string>(type: "text", nullable: false),
                    desired_design_text = table.Column<string>(type: "text", nullable: true),
                    style_preset = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    main_keywords = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    color_preference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    processing_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("chk_products_status", "processing_status IN ('pending', 'validating', 'queued', 'generating_image', 'image_review_required', 'generating_mockup', 'generating_video', 'generating_listing', 'review_required', 'approved', 'exported', 'published', 'failed', 'skipped', 'cancelled')");
                    table.CheckConstraint("chk_products_type", "product_type IN ('tshirt', 'hoodie', 'mug', 'poster', 'tote_bag', 'phone_case')");
                    table.ForeignKey(
                        name: "fk_products_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_products_design_templates_design_template_id",
                        column: x => x.design_template_id,
                        principalTable: "design_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount_usd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    total_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "draft"),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: true),
                    items = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    pdf_url = table.Column<string>(type: "text", nullable: true),
                    stripe_invoice_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("chk_invoices_status", "status IN ('draft', 'issued', 'paid', 'overdue', 'void', 'refunded')");
                    table.CheckConstraint("chk_invoices_total", "total_amount = amount_usd + tax_amount");
                    table.ForeignKey(
                        name: "fk_invoices_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_replies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    support_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reply_text = table.Column<string>(type: "text", nullable: false),
                    is_internal_note = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_replies", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_replies_asp_net_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticket_replies_support_tickets_support_ticket_id",
                        column: x => x.support_ticket_id,
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    delivery_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_deliveries", x => x.id);
                    table.CheckConstraint("chk_notification_channel", "channel IN ('in_app', 'email', 'webhook')");
                    table.CheckConstraint("chk_notification_delivery_status", "delivery_status IN ('pending', 'sent', 'failed', 'skipped')");
                    table.ForeignKey(
                        name: "fk_notification_deliveries_notification_alerts_notification_al",
                        column: x => x.notification_alert_id,
                        principalTable: "notification_alerts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_prompts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    design_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_description = table.Column<string>(type: "text", nullable: false),
                    system_prompt = table.Column<string>(type: "text", nullable: false),
                    few_shot_examples = table.Column<string>(type: "jsonb", nullable: true),
                    generated_prompt = table.Column<string>(type: "text", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    is_approved_by_user = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    user_notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_prompts", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_prompts_design_templates_design_template_id",
                        column: x => x.design_template_id,
                        principalTable: "design_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ai_prompts_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "api_usage_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    feature = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_request_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    request_units = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    tokens_input = table.Column<int>(type: "integer", nullable: true),
                    tokens_output = table.Column<int>(type: "integer", nullable: true),
                    cost_usd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false, defaultValue: 0m),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_usage_records", x => x.id);
                    table.CheckConstraint("chk_api_usage_feature", "feature IN ('image_generation', 'mockup', 'video', 'listing', 'seo', 'integration')");
                    table.CheckConstraint("chk_api_usage_provider", "provider IN ('leonardo', 'stable_diffusion', 'openai', 'gemini', 'printify', 'etsy', 'ffmpeg_local')");
                    table.CheckConstraint("chk_api_usage_status", "status IN ('success', 'failed', 'timeout')");
                    table.ForeignKey(
                        name: "fk_api_usage_records_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_usage_records_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_api_usage_records_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "batch_job_products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sequence_order = table.Column<int>(type: "integer", nullable: false),
                    source_row_index = table.Column<int>(type: "integer", nullable: true),
                    raw_row_data = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    current_step = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_job_products", x => x.id);
                    table.CheckConstraint("chk_batch_job_products_status", "status IN ('pending', 'validating', 'queued', 'generating_image', 'image_review_required', 'generating_mockup', 'generating_video', 'generating_listing', 'review_required', 'approved', 'exported', 'published', 'failed', 'skipped', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_batch_job_products_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_batch_job_products_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "etsy_upload_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    etsy_integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    etsy_listing_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    upload_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    listing_state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "draft"),
                    publish_immediately = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    confirmed_by_user_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    upload_payload = table.Column<string>(type: "jsonb", nullable: false),
                    api_response = table.Column<string>(type: "jsonb", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_etsy_upload_logs", x => x.id);
                    table.CheckConstraint("chk_etsy_listing_state", "listing_state IN ('draft', 'active')");
                    table.CheckConstraint("chk_etsy_publish_needs_confirmation", "publish_immediately = false OR confirmed_by_user_at IS NOT NULL");
                    table.CheckConstraint("chk_etsy_upload_status", "upload_status IN ('pending', 'success', 'failed', 'retrying')");
                    table.ForeignKey(
                        name: "fk_etsy_upload_logs_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_etsy_upload_logs_etsy_integrations_etsy_integration_id",
                        column: x => x.etsy_integration_id,
                        principalTable: "etsy_integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_etsy_upload_logs_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "export_package_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    export_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    include_design_images = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    include_mockup_images = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    include_promo_video = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    include_listing_content = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    folder_path_in_zip = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    item_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_package_items", x => x.id);
                    table.CheckConstraint("chk_export_item_status", "item_status IN ('pending', 'packed', 'failed', 'skipped')");
                    table.ForeignKey(
                        name: "fk_export_package_items_export_packages_export_package_id",
                        column: x => x.export_package_id,
                        principalTable: "export_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_export_package_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listing_contents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    api_usage_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ai_model_used = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    generation_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    approval_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_contents", x => x.id);
                    table.CheckConstraint("chk_listing_contents_approval", "approval_status IN ('pending', 'approved', 'rejected')");
                    table.CheckConstraint("chk_listing_contents_approved_at", "approval_status <> 'approved' OR approved_at IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_listing_contents_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_listing_contents_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "printify_upload_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    printify_integration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    printify_product_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sync_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    upload_payload = table.Column<string>(type: "jsonb", nullable: false),
                    api_response = table.Column<string>(type: "jsonb", nullable: true),
                    upload_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_printify_upload_logs", x => x.id);
                    table.CheckConstraint("chk_printify_sync_type", "sync_type IN ('create', 'update')");
                    table.CheckConstraint("chk_printify_upload_status", "upload_status IN ('pending', 'success', 'failed', 'retrying')");
                    table.ForeignKey(
                        name: "fk_printify_upload_logs_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_printify_upload_logs_printify_integrations_printify_integra",
                        column: x => x.printify_integration_id,
                        principalTable: "printify_integrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_printify_upload_logs_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_mockup_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mockup_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_mockup_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_mockup_templates_mockup_templates_mockup_template_id",
                        column: x => x.mockup_template_id,
                        principalTable: "mockup_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_mockup_templates_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    support_ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticket_reply_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_url = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_size_mb = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_attachments", x => x.id);
                    table.CheckConstraint("chk_attachment_single_owner", "(support_ticket_id IS NOT NULL) <> (ticket_reply_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_ticket_attachments_asp_net_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticket_attachments_support_tickets_support_ticket_id",
                        column: x => x.support_ticket_id,
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ticket_attachments_ticket_replies_ticket_reply_id",
                        column: x => x.ticket_reply_id,
                        principalTable: "ticket_replies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "design_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_prompt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    api_usage_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    image_generator_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    storage_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "s3"),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: false),
                    image_width_px = table.Column<int>(type: "integer", nullable: false),
                    image_height_px = table.Column<int>(type: "integer", nullable: false),
                    file_format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    file_size_mb = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    quality_score = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    user_rating = table.Column<int>(type: "integer", nullable: true),
                    approval_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    is_final = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    variation_index = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    generation_time_seconds = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    generation_metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_images", x => x.id);
                    table.CheckConstraint("chk_design_images_approval", "approval_status IN ('pending', 'approved', 'rejected')");
                    table.CheckConstraint("chk_design_images_quality_score", "quality_score IS NULL OR quality_score BETWEEN 0 AND 1");
                    table.CheckConstraint("chk_design_images_rating", "user_rating IS NULL OR user_rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_design_images_ai_prompts_ai_prompt_id",
                        column: x => x.ai_prompt_id,
                        principalTable: "ai_prompts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_design_images_api_usage_records_api_usage_record_id",
                        column: x => x.api_usage_record_id,
                        principalTable: "api_usage_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_design_images_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_design_images_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promo_videos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    video_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    music_track_id = table.Column<Guid>(type: "uuid", nullable: true),
                    api_usage_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    text_overlay_content = table.Column<string>(type: "text", nullable: true),
                    text_overlay_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    text_overlay_font = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    storage_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValue: "s3"),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    video_url = table.Column<string>(type: "text", nullable: true),
                    video_duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    video_resolution = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    file_format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    aspect_ratio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_size_mb = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    platform_target = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quality_score = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    user_rating = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    approval_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    is_final = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    generation_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_videos", x => x.id);
                    table.CheckConstraint("chk_promo_videos_approval", "approval_status IN ('pending', 'approved', 'rejected')");
                    table.CheckConstraint("chk_promo_videos_duration", "video_duration_seconds BETWEEN 15 AND 30");
                    table.CheckConstraint("chk_promo_videos_quality_score", "quality_score IS NULL OR quality_score BETWEEN 0 AND 1");
                    table.CheckConstraint("chk_promo_videos_rating", "user_rating IS NULL OR user_rating BETWEEN 1 AND 5");
                    table.CheckConstraint("chk_promo_videos_status", "status IN ('pending', 'queued', 'rendering', 'completed', 'failed')");
                    table.ForeignKey(
                        name: "fk_promo_videos_api_usage_records_api_usage_record_id",
                        column: x => x.api_usage_record_id,
                        principalTable: "api_usage_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_promo_videos_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_promo_videos_music_tracks_music_track_id",
                        column: x => x.music_track_id,
                        principalTable: "music_tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_promo_videos_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_promo_videos_video_templates_video_template_id",
                        column: x => x.video_template_id,
                        principalTable: "video_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "batch_job_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_job_product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    api_usage_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    log_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_job_logs", x => x.id);
                    table.CheckConstraint("chk_batch_job_logs_level", "log_level IN ('debug', 'info', 'warning', 'error', 'critical')");
                    table.ForeignKey(
                        name: "fk_batch_job_logs_api_usage_records_api_usage_record_id",
                        column: x => x.api_usage_record_id,
                        principalTable: "api_usage_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_batch_job_logs_batch_job_products_batch_job_product_id",
                        column: x => x.batch_job_product_id,
                        principalTable: "batch_job_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_batch_job_logs_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listing_descriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_content_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_generated_description = table.Column<string>(type: "text", nullable: false),
                    current_description = table.Column<string>(type: "text", nullable: false),
                    is_user_edited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    word_count = table.Column<int>(type: "integer", nullable: false),
                    structure_followed = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    keyword_density_optimal = table.Column<bool>(type: "boolean", nullable: false),
                    seo_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_descriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_listing_descriptions_listing_contents_listing_content_id",
                        column: x => x.listing_content_id,
                        principalTable: "listing_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listing_generation_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_content_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_usage_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    generation_number = table.Column<int>(type: "integer", nullable: false),
                    prompt_used = table.Column<string>(type: "text", nullable: false),
                    adjustment_hint = table.Column<string>(type: "text", nullable: true),
                    raw_ai_response = table.Column<string>(type: "text", nullable: false),
                    title_generated = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    tags_generated = table.Column<string[]>(type: "text[]", nullable: true),
                    description_generated = table.Column<string>(type: "text", nullable: true),
                    user_action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    feedback_notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_generation_history", x => x.id);
                    table.CheckConstraint("chk_listing_history_user_action", "user_action IN ('accepted', 'edited', 'regenerated', 'rejected')");
                    table.ForeignKey(
                        name: "fk_listing_generation_history_api_usage_records_api_usage_reco",
                        column: x => x.api_usage_record_id,
                        principalTable: "api_usage_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_listing_generation_history_listing_contents_listing_content",
                        column: x => x.listing_content_id,
                        principalTable: "listing_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listing_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_content_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_count = table.Column<int>(type: "integer", nullable: false),
                    tag_type_distribution = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    seo_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    is_user_edited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_listing_tags_listing_contents_listing_content_id",
                        column: x => x.listing_content_id,
                        principalTable: "listing_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listing_titles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_content_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_generated_title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    current_title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    is_user_edited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    character_count = table.Column<int>(type: "integer", nullable: false),
                    includes_primary_keyword = table.Column<bool>(type: "boolean", nullable: false),
                    seo_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_titles", x => x.id);
                    table.CheckConstraint("chk_listing_titles_char_count", "character_count = LENGTH(current_title)");
                    table.CheckConstraint("chk_listing_titles_edited_flag", "is_user_edited = (current_title IS DISTINCT FROM ai_generated_title)");
                    table.ForeignKey(
                        name: "fk_listing_titles_listing_contents_listing_content_id",
                        column: x => x.listing_content_id,
                        principalTable: "listing_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seo_scores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_content_id = table.Column<Guid>(type: "uuid", nullable: false),
                    overall_seo_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    title_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    tags_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    description_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    keyword_optimization_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    tag_relevance_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    keyword_density = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    improvement_suggestions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    scoring_algorithm_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "v1"),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_scores", x => x.id);
                    table.CheckConstraint("chk_seo_scores_components", "title_score BETWEEN 0 AND 100 AND tags_score BETWEEN 0 AND 100 AND description_score BETWEEN 0 AND 100 AND keyword_optimization_score BETWEEN 0 AND 100 AND tag_relevance_score BETWEEN 0 AND 100");
                    table.CheckConstraint("chk_seo_scores_range", "overall_seo_score BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_seo_scores_listing_contents_listing_content_id",
                        column: x => x.listing_content_id,
                        principalTable: "listing_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mockup_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    design_image_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mockup_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_usage_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    storage_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "s3"),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    mockup_image_url = table.Column<string>(type: "text", nullable: false),
                    mockup_width_px = table.Column<int>(type: "integer", nullable: false),
                    mockup_height_px = table.Column<int>(type: "integer", nullable: false),
                    generation_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    approval_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    is_final = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mockup_images", x => x.id);
                    table.CheckConstraint("chk_mockup_images_approval", "approval_status IN ('pending', 'approved', 'rejected')");
                    table.ForeignKey(
                        name: "fk_mockup_images_api_usage_records_api_usage_record_id",
                        column: x => x.api_usage_record_id,
                        principalTable: "api_usage_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_mockup_images_design_images_design_image_id",
                        column: x => x.design_image_id,
                        principalTable: "design_images",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mockup_images_mockup_templates_mockup_template_id",
                        column: x => x.mockup_template_id,
                        principalTable: "mockup_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mockup_images_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "social_media_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    promo_video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    share_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    scheduled_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    post_caption = table.Column<string>(type: "text", nullable: true),
                    external_post_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    external_platform_url = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    api_response = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_social_media_shares", x => x.id);
                    table.CheckConstraint("chk_social_share_status", "share_status IN ('pending', 'scheduled', 'posted', 'failed')");
                    table.ForeignKey(
                        name: "fk_social_media_shares_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_social_media_shares_promo_videos_promo_video_id",
                        column: x => x.promo_video_id,
                        principalTable: "promo_videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listing_tag_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_value = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    normalized_value = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tag_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ai"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_tag_items", x => x.id);
                    table.CheckConstraint("chk_tag_position_range", "position BETWEEN 1 AND 13");
                    table.CheckConstraint("chk_tag_source", "source IN ('ai', 'user')");
                    table.CheckConstraint("chk_tag_type", "tag_type IN ('primary', 'long_tail', 'niche', 'occasion', 'product_type')");
                    table.ForeignKey(
                        name: "fk_listing_tag_items_listing_tags_listing_tag_id",
                        column: x => x.listing_tag_id,
                        principalTable: "listing_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "promo_video_scenes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    promo_video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    design_image_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mockup_image_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scene_order = table.Column<int>(type: "integer", nullable: false),
                    duration_seconds = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 3m),
                    transition_effect = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    text_overlay_content = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_video_scenes", x => x.id);
                    table.CheckConstraint("chk_scene_exactly_one_asset", "(design_image_id IS NOT NULL) <> (mockup_image_id IS NOT NULL)");
                    table.CheckConstraint("chk_scene_order_positive", "scene_order > 0");
                    table.CheckConstraint("chk_scene_transition", "transition_effect IS NULL OR transition_effect IN ('fade', 'slide', 'zoom', 'ken_burns', 'none')");
                    table.ForeignKey(
                        name: "fk_promo_video_scenes_design_images_design_image_id",
                        column: x => x.design_image_id,
                        principalTable: "design_images",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_promo_video_scenes_mockup_images_mockup_image_id",
                        column: x => x.mockup_image_id,
                        principalTable: "mockup_images",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_promo_video_scenes_promo_videos_promo_video_id",
                        column: x => x.promo_video_id,
                        principalTable: "promo_videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "share_hashtags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    social_media_share_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hashtag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_hashtags", x => x.id);
                    table.ForeignKey(
                        name: "fk_share_hashtags_social_media_shares_social_media_share_id",
                        column: x => x.social_media_share_id,
                        principalTable: "social_media_shares",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_prompts_design_template_id",
                table: "ai_prompts",
                column: "design_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_prompts_product_id",
                table: "ai_prompts",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "uq_ai_prompts_product_version",
                table: "ai_prompts",
                columns: new[] { "product_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_service_provider",
                table: "api_keys",
                column: "service_provider");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_user_id",
                table: "api_keys",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_usage_records_batch_job_id",
                table: "api_usage_records",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_usage_records_product_id",
                table: "api_usage_records",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_usage_records_provider_feature",
                table: "api_usage_records",
                columns: new[] { "provider", "feature" });

            migrationBuilder.CreateIndex(
                name: "ix_api_usage_records_provider_request_id",
                table: "api_usage_records",
                column: "provider_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_usage_records_user_id_created_at",
                table: "api_usage_records",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_user_id",
                table: "audit_logs",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_at",
                table: "audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_resource_type_resource_id",
                table: "audit_logs",
                columns: new[] { "resource_type", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_auth_tokens_expires_at_utc",
                table: "auth_tokens",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_auth_tokens_replaced_by_token_hash",
                table: "auth_tokens",
                column: "replaced_by_token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_auth_tokens_revoked_at_utc",
                table: "auth_tokens",
                column: "revoked_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_auth_tokens_token_hash",
                table: "auth_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_auth_tokens_user_id_token_type",
                table: "auth_tokens",
                columns: new[] { "user_id", "token_type" });

            migrationBuilder.CreateIndex(
                name: "ix_batch_job_logs_api_usage_record_id",
                table: "batch_job_logs",
                column: "api_usage_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_job_logs_batch_job_id",
                table: "batch_job_logs",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_job_logs_batch_job_product_id",
                table: "batch_job_logs",
                column: "batch_job_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_job_logs_created_at",
                table: "batch_job_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_batch_job_logs_log_level",
                table: "batch_job_logs",
                column: "log_level");

            migrationBuilder.CreateIndex(
                name: "ix_batch_job_products_batch_job_id",
                table: "batch_job_products",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_job_products_batch_job_id_status",
                table: "batch_job_products",
                columns: new[] { "batch_job_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_batch_job_products_product_id",
                table: "batch_job_products",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_job_products_status",
                table: "batch_job_products",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "uq_batch_job_product_order",
                table: "batch_job_products",
                columns: new[] { "batch_job_id", "sequence_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_batch_jobs_created_at",
                table: "batch_jobs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_batch_jobs_started_at",
                table: "batch_jobs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_batch_jobs_status",
                table: "batch_jobs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_batch_jobs_user_id",
                table: "batch_jobs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_jobs_user_id_status",
                table: "batch_jobs",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_design_images_ai_prompt_id",
                table: "design_images",
                column: "ai_prompt_id");

            migrationBuilder.CreateIndex(
                name: "ix_design_images_api_usage_record_id",
                table: "design_images",
                column: "api_usage_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_design_images_approval_status",
                table: "design_images",
                column: "approval_status");

            migrationBuilder.CreateIndex(
                name: "ix_design_images_batch_job_id",
                table: "design_images",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_design_images_created_at",
                table: "design_images",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_design_images_is_final",
                table: "design_images",
                column: "is_final");

            migrationBuilder.CreateIndex(
                name: "ix_design_images_product_id",
                table: "design_images",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_design_templates_art_style",
                table: "design_templates",
                column: "art_style");

            migrationBuilder.CreateIndex(
                name: "ix_design_templates_type",
                table: "design_templates",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "ix_design_templates_user_id",
                table: "design_templates",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_etsy_integrations_etsy_shop_id",
                table: "etsy_integrations",
                column: "etsy_shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_etsy_integrations_user_id",
                table: "etsy_integrations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_etsy_upload_logs_batch_job_id",
                table: "etsy_upload_logs",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_etsy_upload_logs_etsy_integration_id",
                table: "etsy_upload_logs",
                column: "etsy_integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_etsy_upload_logs_product_id",
                table: "etsy_upload_logs",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_etsy_upload_logs_upload_status",
                table: "etsy_upload_logs",
                column: "upload_status");

            migrationBuilder.CreateIndex(
                name: "uq_etsy_idempotency",
                table: "etsy_upload_logs",
                columns: new[] { "etsy_integration_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_export_package_items_item_status",
                table: "export_package_items",
                column: "item_status");

            migrationBuilder.CreateIndex(
                name: "ix_export_package_items_product_id",
                table: "export_package_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "uq_export_package_product",
                table: "export_package_items",
                columns: new[] { "export_package_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_export_packages_batch_job_id",
                table: "export_packages",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_export_packages_expires_at",
                table: "export_packages",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_export_packages_status",
                table: "export_packages",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_export_packages_user_id",
                table: "export_packages",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_created_at",
                table: "invoices",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_invoice_number",
                table: "invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_status",
                table: "invoices",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_subscription_id",
                table: "invoices",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_user_id",
                table: "invoices",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_contents_approval_status",
                table: "listing_contents",
                column: "approval_status");

            migrationBuilder.CreateIndex(
                name: "ix_listing_contents_batch_job_id",
                table: "listing_contents",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_contents_product_id",
                table: "listing_contents",
                column: "product_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_listing_descriptions_version",
                table: "listing_descriptions",
                columns: new[] { "listing_content_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listing_generation_history_api_usage_record_id",
                table: "listing_generation_history",
                column: "api_usage_record_id");

            migrationBuilder.CreateIndex(
                name: "uq_listing_generation_number",
                table: "listing_generation_history",
                columns: new[] { "listing_content_id", "generation_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listing_tag_items_normalized_value",
                table: "listing_tag_items",
                column: "normalized_value");

            migrationBuilder.CreateIndex(
                name: "uq_listing_tag_no_duplicate",
                table: "listing_tag_items",
                columns: new[] { "listing_tag_id", "normalized_value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_listing_tag_position",
                table: "listing_tag_items",
                columns: new[] { "listing_tag_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_listing_tags_version",
                table: "listing_tags",
                columns: new[] { "listing_content_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_listing_titles_version",
                table: "listing_titles",
                columns: new[] { "listing_content_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mockup_images_api_usage_record_id",
                table: "mockup_images",
                column: "api_usage_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_images_design_image_id",
                table: "mockup_images",
                column: "design_image_id");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_images_mockup_template_id",
                table: "mockup_images",
                column: "mockup_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_images_product_id",
                table: "mockup_images",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_templates_is_active",
                table: "mockup_templates",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_templates_product_type",
                table: "mockup_templates",
                column: "product_type");

            migrationBuilder.CreateIndex(
                name: "ix_music_tracks_genre",
                table: "music_tracks",
                column: "genre");

            migrationBuilder.CreateIndex(
                name: "ix_music_tracks_mood",
                table: "music_tracks",
                column: "mood");

            migrationBuilder.CreateIndex(
                name: "ix_notification_alerts_batch_job_id",
                table: "notification_alerts",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_alerts_created_at",
                table: "notification_alerts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_notification_alerts_is_read",
                table: "notification_alerts",
                column: "is_read");

            migrationBuilder.CreateIndex(
                name: "ix_notification_alerts_severity",
                table: "notification_alerts",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "ix_notification_alerts_user_id",
                table: "notification_alerts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_delivery_status",
                table: "notification_deliveries",
                column: "delivery_status");

            migrationBuilder.CreateIndex(
                name: "uq_notification_delivery_channel",
                table: "notification_deliveries",
                columns: new[] { "notification_alert_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_user_id",
                table: "payment_methods",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_permissions_code",
                table: "permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permissions_resource_action",
                table: "permissions",
                columns: new[] { "resource", "action" });

            migrationBuilder.CreateIndex(
                name: "ix_printify_integrations_printify_store_id",
                table: "printify_integrations",
                column: "printify_store_id");

            migrationBuilder.CreateIndex(
                name: "ix_printify_integrations_user_id",
                table: "printify_integrations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_printify_upload_logs_batch_job_id",
                table: "printify_upload_logs",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_printify_upload_logs_printify_integration_id",
                table: "printify_upload_logs",
                column: "printify_integration_id");

            migrationBuilder.CreateIndex(
                name: "ix_printify_upload_logs_product_id",
                table: "printify_upload_logs",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_printify_upload_logs_upload_status",
                table: "printify_upload_logs",
                column: "upload_status");

            migrationBuilder.CreateIndex(
                name: "uq_printify_idempotency",
                table: "printify_upload_logs",
                columns: new[] { "printify_integration_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_mockup_templates_mockup_template_id",
                table: "product_mockup_templates",
                column: "mockup_template_id");

            migrationBuilder.CreateIndex(
                name: "uq_product_mockup_template",
                table: "product_mockup_templates",
                columns: new[] { "product_id", "mockup_template_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_created_at",
                table: "products",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_products_design_template_id",
                table: "products",
                column: "design_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_processing_status",
                table: "products",
                column: "processing_status");

            migrationBuilder.CreateIndex(
                name: "ix_products_user_id",
                table: "products",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_promo_video_scenes_design_image_id",
                table: "promo_video_scenes",
                column: "design_image_id");

            migrationBuilder.CreateIndex(
                name: "ix_promo_video_scenes_mockup_image_id",
                table: "promo_video_scenes",
                column: "mockup_image_id");

            migrationBuilder.CreateIndex(
                name: "uq_promo_video_scene_order",
                table: "promo_video_scenes",
                columns: new[] { "promo_video_id", "scene_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_promo_videos_api_usage_record_id",
                table: "promo_videos",
                column: "api_usage_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_promo_videos_approval_status",
                table: "promo_videos",
                column: "approval_status");

            migrationBuilder.CreateIndex(
                name: "ix_promo_videos_batch_job_id",
                table: "promo_videos",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_promo_videos_created_at",
                table: "promo_videos",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_promo_videos_music_track_id",
                table: "promo_videos",
                column: "music_track_id");

            migrationBuilder.CreateIndex(
                name: "ix_promo_videos_product_id",
                table: "promo_videos",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_promo_videos_status",
                table: "promo_videos",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_promo_videos_video_template_id",
                table: "promo_videos",
                column: "video_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_normalized_code",
                table: "roles",
                column: "normalized_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_scores_listing_content_id",
                table: "seo_scores",
                column: "listing_content_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seo_scores_overall_seo_score",
                table: "seo_scores",
                column: "overall_seo_score");

            migrationBuilder.CreateIndex(
                name: "uq_share_hashtag_position",
                table: "share_hashtags",
                columns: new[] { "social_media_share_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_social_media_shares_platform",
                table: "social_media_shares",
                column: "platform");

            migrationBuilder.CreateIndex(
                name: "ix_social_media_shares_product_id",
                table: "social_media_shares",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_social_media_shares_promo_video_id",
                table: "social_media_shares",
                column: "promo_video_id");

            migrationBuilder.CreateIndex(
                name: "ix_social_media_shares_share_status",
                table: "social_media_shares",
                column: "share_status");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_plans_is_active",
                table: "subscription_plans",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_plans_tier",
                table: "subscription_plans",
                column: "tier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_created_at",
                table: "subscriptions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_plan_id",
                table: "subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_renewal_date",
                table: "subscriptions",
                column: "renewal_date");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status",
                table: "subscriptions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_user_id",
                table: "subscriptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_assigned_to",
                table: "support_tickets",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_created_at",
                table: "support_tickets",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_priority",
                table: "support_tickets",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_status",
                table: "support_tickets",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_ticket_number",
                table: "support_tickets",
                column: "ticket_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_user_id",
                table: "support_tickets",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_attachments_support_ticket_id",
                table: "ticket_attachments",
                column: "support_ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_attachments_ticket_reply_id",
                table: "ticket_attachments",
                column: "ticket_reply_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_attachments_uploaded_by",
                table: "ticket_attachments",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_replies_author_id",
                table: "ticket_replies",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_replies_support_ticket_id",
                table: "ticket_replies",
                column: "support_ticket_id");

            migrationBuilder.CreateIndex(
                name: "uq_usage_statistics_period",
                table: "usage_statistics",
                columns: new[] { "user_id", "billing_period_start", "billing_period_end" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_profiles_user_id",
                table: "user_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_role_history_performed_by",
                table: "user_role_history",
                column: "performed_by");

            migrationBuilder.CreateIndex(
                name: "ix_user_role_history_role_id",
                table: "user_role_history",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_role_history_user_id",
                table: "user_role_history",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_granted_by",
                table: "user_roles",
                column: "granted_by");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_account_status",
                table: "users",
                column: "account_status");

            migrationBuilder.CreateIndex(
                name: "ix_users_deleted_at",
                table: "users",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                table: "users",
                column: "normalized_user_name",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_oauth_google_id",
                table: "users",
                column: "oauth_google_id",
                unique: true,
                filter: "oauth_google_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_video_templates_platform",
                table: "video_templates",
                column: "platform");

            migrationBuilder.CreateIndex(
                name: "ix_video_templates_type",
                table: "video_templates",
                column: "type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "auth_tokens");

            migrationBuilder.DropTable(
                name: "batch_job_logs");

            migrationBuilder.DropTable(
                name: "etsy_upload_logs");

            migrationBuilder.DropTable(
                name: "export_package_items");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "listing_descriptions");

            migrationBuilder.DropTable(
                name: "listing_generation_history");

            migrationBuilder.DropTable(
                name: "listing_tag_items");

            migrationBuilder.DropTable(
                name: "listing_titles");

            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "payment_methods");

            migrationBuilder.DropTable(
                name: "plan_features");

            migrationBuilder.DropTable(
                name: "printify_upload_logs");

            migrationBuilder.DropTable(
                name: "product_mockup_templates");

            migrationBuilder.DropTable(
                name: "promo_video_scenes");

            migrationBuilder.DropTable(
                name: "role_claims");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "seo_scores");

            migrationBuilder.DropTable(
                name: "share_hashtags");

            migrationBuilder.DropTable(
                name: "ticket_attachments");

            migrationBuilder.DropTable(
                name: "usage_statistics");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_profiles");

            migrationBuilder.DropTable(
                name: "user_role_history");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "batch_job_products");

            migrationBuilder.DropTable(
                name: "etsy_integrations");

            migrationBuilder.DropTable(
                name: "export_packages");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "listing_tags");

            migrationBuilder.DropTable(
                name: "notification_alerts");

            migrationBuilder.DropTable(
                name: "printify_integrations");

            migrationBuilder.DropTable(
                name: "mockup_images");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "social_media_shares");

            migrationBuilder.DropTable(
                name: "ticket_replies");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "subscription_plans");

            migrationBuilder.DropTable(
                name: "listing_contents");

            migrationBuilder.DropTable(
                name: "design_images");

            migrationBuilder.DropTable(
                name: "mockup_templates");

            migrationBuilder.DropTable(
                name: "promo_videos");

            migrationBuilder.DropTable(
                name: "support_tickets");

            migrationBuilder.DropTable(
                name: "ai_prompts");

            migrationBuilder.DropTable(
                name: "api_usage_records");

            migrationBuilder.DropTable(
                name: "music_tracks");

            migrationBuilder.DropTable(
                name: "video_templates");

            migrationBuilder.DropTable(
                name: "batch_jobs");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "design_templates");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
