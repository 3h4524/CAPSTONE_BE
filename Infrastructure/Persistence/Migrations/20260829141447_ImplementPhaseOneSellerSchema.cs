using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace APCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementPhaseOneSellerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // UUID primary keys cannot be converted safely to generated integer keys while
            // retaining Identity relationships. This foundation migration intentionally resets
            // existing authentication sessions/accounts before creating the seller schema.
            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "role_claims");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateTable(
                name: "sellers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    oauth_google_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    oauth_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "UTC"),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "en"),
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
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sellers", x => x.id);
                    table.CheckConstraint("ck_sellers_account_status", "account_status IN ('active', 'suspended', 'deactivated')");
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    jwt_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    replaced_by_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    reason_revoked = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expires_at_utc",
                table: "refresh_tokens",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_revoked_at_utc",
                table: "refresh_tokens",
                column: "revoked_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_seller_id",
                table: "refresh_tokens",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
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
                    features = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
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
                    table.CheckConstraint("ck_subscription_plans_annual_price", "annual_price_usd IS NULL OR annual_price_usd >= 0");
                    table.CheckConstraint("ck_subscription_plans_monthly_price", "monthly_price_usd >= 0");
                    table.CheckConstraint("ck_subscription_plans_quotas", "max_batch_size >= 0 AND max_products_per_month >= 0 AND max_concurrent_jobs >= 0 AND image_generation_quota >= 0 AND video_generation_quota >= 0 AND api_call_quota >= 0 AND storage_quota_gb >= 0");
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    service_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    key_identifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    key_value_encrypted = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    usage_limit = table.Column<int>(type: "integer", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.CheckConstraint("ck_api_keys_usage_count", "usage_count >= 0");
                    table.CheckConstraint("ck_api_keys_usage_limit", "usage_limit IS NULL OR usage_limit >= 0");
                    table.ForeignKey(
                        name: "fk_api_keys_seller_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    payment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    stripe_payment_method_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    paypal_email_encrypted = table.Column<string>(type: "text", nullable: true),
                    card_last_4_digits = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    card_brand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_methods", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_methods_seller_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seller_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seller_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_seller_claims_sellers_user_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seller_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    seller_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seller_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_seller_logins_sellers_user_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seller_profiles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    shop_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    shop_description = table.Column<string>(type: "text", nullable: true),
                    notification_email_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    newsletter_subscribed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    default_timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "UTC"),
                    default_language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "en"),
                    theme_preference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "light"),
                    profile_completion_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seller_profiles", x => x.id);
                    table.CheckConstraint("ck_seller_profiles_completion", "profile_completion_percentage BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_seller_profiles_seller_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seller_roles",
                columns: table => new
                {
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    role_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seller_roles", x => new { x.seller_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_seller_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_seller_roles_sellers_user_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seller_tokens",
                columns: table => new
                {
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seller_tokens", x => new { x.seller_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_seller_tokens_sellers_user_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usage_statistics",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    billing_period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    billing_period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    images_generated = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    videos_created = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    listings_exported = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_api_calls = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_api_cost_usd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false, defaultValue: 0m),
                    storage_used_gb = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    batch_jobs_completed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    average_processing_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_statistics", x => x.id);
                    table.CheckConstraint("ck_usage_statistics_costs", "total_api_cost_usd >= 0 AND storage_used_gb >= 0 AND average_processing_time_seconds >= 0");
                    table.CheckConstraint("ck_usage_statistics_counters", "images_generated >= 0 AND videos_created >= 0 AND listings_exported >= 0 AND total_api_calls >= 0 AND batch_jobs_completed >= 0");
                    table.CheckConstraint("ck_usage_statistics_period", "billing_period_end >= billing_period_start");
                    table.ForeignKey(
                        name: "fk_usage_statistics_seller_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    plan_id = table.Column<int>(type: "integer", nullable: false),
                    billing_cycle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    monthly_price_usd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    annual_price_usd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "active"),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    renewal_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    auto_renew = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.CheckConstraint("ck_subscriptions_dates", "renewal_date >= start_date");
                    table.CheckConstraint("ck_subscriptions_prices", "monthly_price_usd >= 0 AND (annual_price_usd IS NULL OR annual_price_usd >= 0)");
                    table.ForeignKey(
                        name: "fk_subscriptions_seller_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_subscriptions_subscription_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscription_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    subscription_id = table.Column<int>(type: "integer", nullable: false),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
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
                    table.CheckConstraint("ck_invoices_amounts", "amount_usd >= 0 AND tax_amount >= 0 AND total_amount >= 0");
                    table.CheckConstraint("ck_invoices_due_date", "due_date >= invoice_date");
                    table.ForeignKey(
                        name: "fk_invoices_seller_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invoices_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_seller_id",
                table: "api_keys",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_seller_id_service_provider",
                table: "api_keys",
                columns: new[] { "seller_id", "service_provider" },
                unique: true,
                filter: "is_active = TRUE AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_service_provider",
                table: "api_keys",
                column: "service_provider");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_created_at",
                table: "invoices",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_invoice_number",
                table: "invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_seller_id",
                table: "invoices",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_status",
                table: "invoices",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_subscription_id",
                table: "invoices",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_one_default_per_seller",
                table: "payment_methods",
                column: "seller_id",
                unique: true,
                filter: "is_default = TRUE AND is_active = TRUE AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_seller_claims_user_id",
                table: "seller_claims",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_seller_logins_user_id",
                table: "seller_logins",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_seller_profiles_seller_id",
                table: "seller_profiles",
                column: "seller_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seller_roles_role_id",
                table: "seller_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "sellers",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_sellers_account_status",
                table: "sellers",
                column: "account_status");

            migrationBuilder.CreateIndex(
                name: "ix_sellers_deleted_at",
                table: "sellers",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "ix_sellers_email",
                table: "sellers",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sellers_oauth_google_id",
                table: "sellers",
                column: "oauth_google_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "sellers",
                column: "normalized_user_name",
                unique: true);

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
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_plan_id",
                table: "subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_renewal_date",
                table: "subscriptions",
                column: "renewal_date");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_seller_id",
                table: "subscriptions",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status",
                table: "subscriptions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_usage_statistics_billing_period_start_billing_period_end",
                table: "usage_statistics",
                columns: new[] { "billing_period_start", "billing_period_end" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_statistics_seller_id",
                table: "usage_statistics",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_usage_statistics_seller_id_billing_period_start_billing_per",
                table: "usage_statistics",
                columns: new[] { "seller_id", "billing_period_start", "billing_period_end" },
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "payment_methods");

            migrationBuilder.DropTable(
                name: "seller_claims");

            migrationBuilder.DropTable(
                name: "seller_logins");

            migrationBuilder.DropTable(
                name: "seller_profiles");

            migrationBuilder.DropTable(
                name: "seller_roles");

            migrationBuilder.DropTable(
                name: "seller_tokens");

            migrationBuilder.DropTable(
                name: "usage_statistics");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "sellers");

            migrationBuilder.DropTable(
                name: "subscription_plans");

            migrationBuilder.DropTable(
                name: "role_claims");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
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
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    avatar_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    full_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    last_login_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    jwt_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    replaced_by_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    reason_revoked = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_expires_at_utc",
                table: "refresh_tokens",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_revoked_at_utc",
                table: "refresh_tokens",
                column: "revoked_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
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
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "users",
                column: "normalized_user_name",
                unique: true);

        }
    }
}
