using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace APCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementRemainingSchemaEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admins",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    permissions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admins", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "batch_jobs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
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
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estimated_completion_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    actual_cost_usd = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_jobs", x => x.id);
                    table.CheckConstraint("ck_batch_jobs_costs", "estimated_cost_usd >= 0 AND actual_cost_usd >= 0");
                    table.CheckConstraint("ck_batch_jobs_counts", "total_products >= 0 AND processed_products >= 0 AND failed_products >= 0 AND skipped_products >= 0");
                    table.CheckConstraint("ck_batch_jobs_progress", "progress_percentage BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_batch_jobs_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "design_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    niche_category = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    art_style = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    base_prompt = table.Column<string>(type: "text", nullable: false),
                    example_prompts = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    style_description = table.Column<string>(type: "text", nullable: true),
                    preview_image_url = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_templates", x => x.id);
                    table.CheckConstraint("ck_design_templates_usage_count", "usage_count >= 0");
                    table.ForeignKey(
                        name: "fk_design_templates_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "etsy_integrations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    etsy_shop_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    etsy_oauth_token_encrypted = table.Column<string>(type: "text", nullable: false),
                    shop_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
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
                    table.CheckConstraint("ck_etsy_integrations_totals", "total_listings_created >= 0 AND total_listings_updated >= 0");
                    table.ForeignKey(
                        name: "fk_etsy_integrations_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "music_tracks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    artist_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    genre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mood = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    royalty_free = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    license_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    audio_url = table.Column<string>(type: "text", nullable: false),
                    preview_url = table.Column<string>(type: "text", nullable: true),
                    waveform_data = table.Column<string>(type: "jsonb", nullable: true),
                    is_available = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_music_tracks", x => x.id);
                    table.CheckConstraint("ck_music_tracks_duration", "duration_seconds > 0");
                });

            migrationBuilder.CreateTable(
                name: "printify_integrations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    printify_store_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    printify_api_token_encrypted = table.Column<string>(type: "text", nullable: false),
                    shop_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    shop_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
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
                    table.CheckConstraint("ck_printify_integrations_total_products", "total_products_uploaded >= 0");
                    table.ForeignKey(
                        name: "fk_printify_integrations_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_metrics",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    metric_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metric_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    value = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    dimension = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_metrics", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "video_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
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
                    table.CheckConstraint("ck_video_templates_duration", "duration_seconds > 0");
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: true),
                    admin_id = table.Column<int>(type: "integer", nullable: true),
                    action_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_id = table.Column<int>(type: "integer", nullable: false),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_logs_admins_admin_id",
                        column: x => x.admin_id,
                        principalTable: "admins",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_audit_logs_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    ticket_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "normal"),
                    assigned_to = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "open"),
                    attachment_urls = table.Column<string[]>(type: "text[]", nullable: true),
                    satisfaction_rating = table.Column<int>(type: "integer", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_tickets", x => x.id);
                    table.CheckConstraint("ck_support_tickets_satisfaction", "satisfaction_rating IS NULL OR satisfaction_rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_support_tickets_admins_assigned_to_admin_id",
                        column: x => x.assigned_to,
                        principalTable: "admins",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_support_tickets_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_configurations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    config_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    config_value = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_modified_by = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_configurations", x => x.id);
                    table.ForeignKey(
                        name: "fk_system_configurations_admins_last_modified_by_admin_id",
                        column: x => x.last_modified_by,
                        principalTable: "admins",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "export_packages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    batch_job_id = table.Column<int>(type: "integer", nullable: false),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    product_ids = table.Column<int[]>(type: "integer[]", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    package_content = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    file_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    download_url = table.Column<string>(type: "text", nullable: true),
                    file_size_mb = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    creation_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "preparing"),
                    downloaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_packages", x => x.id);
                    table.CheckConstraint("ck_export_packages_creation_time", "creation_time_seconds >= 0");
                    table.CheckConstraint("ck_export_packages_file_size", "file_size_mb IS NULL OR file_size_mb >= 0");
                    table.ForeignKey(
                        name: "fk_export_packages_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_export_packages_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_alerts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    batch_job_id = table.Column<int>(type: "integer", nullable: true),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    notification_channels = table.Column<string[]>(type: "character varying(50)[]", nullable: false, defaultValueSql: "ARRAY['in_app']::character varying(50)[]"),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    action_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_alerts_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_notification_alerts_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    seller_id = table.Column<int>(type: "integer", nullable: false),
                    batch_job_id = table.Column<int>(type: "integer", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    product_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    niche_category = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    input_description = table.Column<string>(type: "text", nullable: false),
                    processing_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_products_batch_jobs_batch_job_id",
                        column: x => x.batch_job_id,
                        principalTable: "batch_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_products_sellers_seller_id",
                        column: x => x.seller_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_replies",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    support_ticket_id = table.Column<int>(type: "integer", nullable: false),
                    author_id = table.Column<int>(type: "integer", nullable: false),
                    author_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reply_text = table.Column<string>(type: "text", nullable: false),
                    attachment_urls = table.Column<string[]>(type: "text[]", nullable: true),
                    is_internal_note = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_replies", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_replies_sellers_author_id",
                        column: x => x.author_id,
                        principalTable: "sellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ticket_replies_support_tickets_support_ticket_id",
                        column: x => x.support_ticket_id,
                        principalTable: "support_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_prompts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    design_template_id = table.Column<int>(type: "integer", nullable: true),
                    original_description = table.Column<string>(type: "text", nullable: false),
                    system_prompt = table.Column<string>(type: "text", nullable: false),
                    few_shot_examples = table.Column<string>(type: "jsonb", nullable: true),
                    generated_prompt = table.Column<string>(type: "text", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    is_approved_by_user = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    user_notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_prompts", x => x.id);
                    table.CheckConstraint("ck_ai_prompts_version", "version_number > 0");
                    table.ForeignKey(
                        name: "fk_ai_prompts_design_templates_design_template_id",
                        column: x => x.design_template_id,
                        principalTable: "design_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ai_prompts_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "batch_job_products",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    batch_job_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: true),
                    sequence_order = table.Column<int>(type: "integer", nullable: false),
                    source_row_index = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
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
                    table.CheckConstraint("ck_batch_job_products_duration", "duration_seconds IS NULL OR duration_seconds >= 0");
                    table.CheckConstraint("ck_batch_job_products_retry_count", "retry_count >= 0");
                    table.CheckConstraint("ck_batch_job_products_sequence", "sequence_order >= 0");
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
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    etsy_integration_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    batch_job_id = table.Column<int>(type: "integer", nullable: true),
                    etsy_listing_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    upload_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    publish_immediately = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
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
                    table.CheckConstraint("ck_etsy_upload_logs_retry_count", "retry_count >= 0");
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
                name: "listing_contents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    batch_job_id = table.Column<int>(type: "integer", nullable: true),
                    ai_model_used = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    generation_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    api_cost_usd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    approval_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_contents", x => x.id);
                    table.CheckConstraint("ck_listing_contents_cost", "generation_time_seconds >= 0 AND api_cost_usd >= 0");
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
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    printify_integration_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    batch_job_id = table.Column<int>(type: "integer", nullable: true),
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
                    table.CheckConstraint("ck_printify_upload_logs_retry_count", "retry_count >= 0");
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
                name: "promo_videos",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    batch_job_id = table.Column<int>(type: "integer", nullable: true),
                    video_template_id = table.Column<int>(type: "integer", nullable: false),
                    design_image_ids = table.Column<int[]>(type: "integer[]", nullable: false),
                    music_track_id = table.Column<int>(type: "integer", nullable: true),
                    text_overlay_content = table.Column<string>(type: "text", nullable: true),
                    text_overlay_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    text_overlay_font = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    video_url = table.Column<string>(type: "text", nullable: true),
                    video_local_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    api_cost_usd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_videos", x => x.id);
                    table.CheckConstraint("ck_promo_videos_cost", "api_cost_usd >= 0");
                    table.CheckConstraint("ck_promo_videos_duration", "video_duration_seconds > 0 AND (generation_time_seconds IS NULL OR generation_time_seconds >= 0)");
                    table.CheckConstraint("ck_promo_videos_file_size", "file_size_mb IS NULL OR file_size_mb >= 0");
                    table.CheckConstraint("ck_promo_videos_quality", "quality_score IS NULL OR quality_score BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_promo_videos_rating", "user_rating IS NULL OR user_rating BETWEEN 1 AND 5");
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
                        onDelete: ReferentialAction.Restrict);
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
                name: "design_images",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    ai_prompt_id = table.Column<int>(type: "integer", nullable: false),
                    batch_job_id = table.Column<int>(type: "integer", nullable: true),
                    image_generator_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    api_response_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: false),
                    image_local_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    api_cost_usd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    generation_metadata = table.Column<string>(type: "jsonb", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_design_images", x => x.id);
                    table.CheckConstraint("ck_design_images_cost", "generation_time_seconds >= 0 AND api_cost_usd >= 0");
                    table.CheckConstraint("ck_design_images_dimensions", "image_width_px > 0 AND image_height_px > 0");
                    table.CheckConstraint("ck_design_images_file_size", "file_size_mb >= 0");
                    table.CheckConstraint("ck_design_images_quality", "quality_score IS NULL OR quality_score BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_design_images_rating", "user_rating IS NULL OR user_rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_design_images_ai_prompts_ai_prompt_id",
                        column: x => x.ai_prompt_id,
                        principalTable: "ai_prompts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "batch_job_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    batch_job_id = table.Column<int>(type: "integer", nullable: false),
                    batch_job_product_id = table.Column<int>(type: "integer", nullable: true),
                    log_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    api_call_identifier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_job_logs", x => x.id);
                    table.CheckConstraint("ck_batch_job_logs_duration", "duration_ms IS NULL OR duration_ms >= 0");
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
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    listing_content_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    generated_description = table.Column<string>(type: "text", nullable: false),
                    word_count = table.Column<int>(type: "integer", nullable: false),
                    structure_followed = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    seo_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    keyword_density_optimal = table.Column<bool>(type: "boolean", nullable: false),
                    user_edited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    user_edited_version = table.Column<string>(type: "text", nullable: true),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_descriptions", x => x.id);
                    table.CheckConstraint("ck_listing_descriptions_seo_score", "seo_score BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_listing_descriptions_version", "version_number > 0");
                    table.CheckConstraint("ck_listing_descriptions_word_count", "word_count BETWEEN 200 AND 500");
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
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    listing_content_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    generation_number = table.Column<int>(type: "integer", nullable: false),
                    prompt_used = table.Column<string>(type: "text", nullable: false),
                    raw_ai_response = table.Column<string>(type: "text", nullable: false),
                    title_generated = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    tags_generated = table.Column<string[]>(type: "text[]", nullable: true),
                    description_generated = table.Column<string>(type: "text", nullable: true),
                    api_response_time_ms = table.Column<int>(type: "integer", nullable: false),
                    api_tokens_used = table.Column<int>(type: "integer", nullable: false),
                    api_cost_usd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    user_action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    feedback_notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_generation_history", x => x.id);
                    table.CheckConstraint("ck_listing_generation_history_api", "api_response_time_ms >= 0 AND api_tokens_used >= 0 AND api_cost_usd >= 0");
                    table.CheckConstraint("ck_listing_generation_history_number", "generation_number > 0");
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
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    listing_content_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    generated_tags = table.Column<string[]>(type: "text[]", nullable: false),
                    tag_count = table.Column<int>(type: "integer", nullable: false),
                    tag_types = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    seo_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    user_edited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    user_edited_version = table.Column<string[]>(type: "text[]", nullable: true),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_tags", x => x.id);
                    table.CheckConstraint("ck_listing_tags_count", "tag_count = 13");
                    table.CheckConstraint("ck_listing_tags_seo_score", "seo_score BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_listing_tags_version", "version_number > 0");
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
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    listing_content_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    generated_title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    character_count = table.Column<int>(type: "integer", nullable: false),
                    includes_primary_keyword = table.Column<bool>(type: "boolean", nullable: false),
                    seo_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    user_edited = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    user_edited_version = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    version_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_titles", x => x.id);
                    table.CheckConstraint("ck_listing_titles_character_count", "character_count BETWEEN 0 AND 140");
                    table.CheckConstraint("ck_listing_titles_seo_score", "seo_score BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_listing_titles_version", "version_number > 0");
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
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    listing_content_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    overall_seo_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    title_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    tags_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    description_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    keyword_optimization_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    tag_relevance_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    keyword_density = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    improvement_suggestions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seo_scores", x => x.id);
                    table.CheckConstraint("ck_seo_scores_ranges", "overall_seo_score BETWEEN 0 AND 100 AND title_score BETWEEN 0 AND 100 AND tags_score BETWEEN 0 AND 100 AND description_score BETWEEN 0 AND 100 AND keyword_optimization_score BETWEEN 0 AND 100 AND tag_relevance_score BETWEEN 0 AND 100 AND keyword_density BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_seo_scores_listing_contents_listing_content_id",
                        column: x => x.listing_content_id,
                        principalTable: "listing_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "social_media_shares",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    promo_video_id = table.Column<int>(type: "integer", nullable: false),
                    platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    share_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "pending"),
                    scheduled_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    post_caption = table.Column<string>(type: "text", nullable: true),
                    hashtags = table.Column<string[]>(type: "text[]", nullable: true),
                    external_post_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    external_platform_url = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    api_response = table.Column<string>(type: "jsonb", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_social_media_shares", x => x.id);
                    table.CheckConstraint("ck_social_media_shares_retry_count", "retry_count >= 0");
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
                name: "mockup_images",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    design_image_id = table.Column<int>(type: "integer", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    mockup_template_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mockup_image_url = table.Column<string>(type: "text", nullable: false),
                    mockup_local_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    mockup_width_px = table.Column<int>(type: "integer", nullable: false),
                    mockup_height_px = table.Column<int>(type: "integer", nullable: false),
                    generation_time_seconds = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    api_cost_usd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    is_final = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mockup_images", x => x.id);
                    table.CheckConstraint("ck_mockup_images_cost", "(generation_time_seconds IS NULL OR generation_time_seconds >= 0) AND (api_cost_usd IS NULL OR api_cost_usd >= 0)");
                    table.CheckConstraint("ck_mockup_images_dimensions", "mockup_width_px > 0 AND mockup_height_px > 0");
                    table.ForeignKey(
                        name: "fk_mockup_images_design_images_design_image_id",
                        column: x => x.design_image_id,
                        principalTable: "design_images",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_mockup_images_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_admins_email",
                table: "admins",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admins_role",
                table: "admins",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "ix_ai_prompts_design_template_id",
                table: "ai_prompts",
                column: "design_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_prompts_product_id",
                table: "ai_prompts",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_prompts_product_id_version_number",
                table: "ai_prompts",
                columns: new[] { "product_id", "version_number" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_admin_id",
                table: "audit_logs",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_at",
                table: "audit_logs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_resource_type_resource_id",
                table: "audit_logs",
                columns: new[] { "resource_type", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_seller_id",
                table: "audit_logs",
                column: "seller_id");

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
                column: "created_at",
                descending: new bool[0]);

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
                name: "ix_batch_jobs_created_at",
                table: "batch_jobs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_batch_jobs_seller_id",
                table: "batch_jobs",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_jobs_seller_id_status",
                table: "batch_jobs",
                columns: new[] { "seller_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_batch_jobs_started_at",
                table: "batch_jobs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_batch_jobs_status",
                table: "batch_jobs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_design_images_ai_prompt_id",
                table: "design_images",
                column: "ai_prompt_id");

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
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_design_images_is_final",
                table: "design_images",
                column: "is_final");

            migrationBuilder.CreateIndex(
                name: "ix_design_images_product_id",
                table: "design_images",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_design_images_quality_score",
                table: "design_images",
                column: "quality_score",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_design_templates_art_style",
                table: "design_templates",
                column: "art_style");

            migrationBuilder.CreateIndex(
                name: "ix_design_templates_seller_id",
                table: "design_templates",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_design_templates_type",
                table: "design_templates",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "ix_etsy_integrations_etsy_shop_id",
                table: "etsy_integrations",
                column: "etsy_shop_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_etsy_integrations_seller_id",
                table: "etsy_integrations",
                column: "seller_id",
                unique: true);

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
                name: "ix_export_packages_batch_job_id",
                table: "export_packages",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_export_packages_expires_at",
                table: "export_packages",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_export_packages_seller_id",
                table: "export_packages",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_export_packages_status",
                table: "export_packages",
                column: "status");

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
                name: "ix_listing_descriptions_listing_content_id",
                table: "listing_descriptions",
                column: "listing_content_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_generation_history_listing_content_id",
                table: "listing_generation_history",
                column: "listing_content_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_generation_history_listing_content_id_generation_nu",
                table: "listing_generation_history",
                columns: new[] { "listing_content_id", "generation_number" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_listing_tags_listing_content_id",
                table: "listing_tags",
                column: "listing_content_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_titles_listing_content_id",
                table: "listing_titles",
                column: "listing_content_id");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_images_design_image_id",
                table: "mockup_images",
                column: "design_image_id");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_images_product_id",
                table: "mockup_images",
                column: "product_id");

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
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_notification_alerts_is_read",
                table: "notification_alerts",
                column: "is_read");

            migrationBuilder.CreateIndex(
                name: "ix_notification_alerts_seller_id",
                table: "notification_alerts",
                column: "seller_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_alerts_severity",
                table: "notification_alerts",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "ix_printify_integrations_printify_store_id",
                table: "printify_integrations",
                column: "printify_store_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_printify_integrations_seller_id",
                table: "printify_integrations",
                column: "seller_id",
                unique: true);

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
                name: "ix_products_batch_job_id",
                table: "products",
                column: "batch_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_created_at",
                table: "products",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_products_processing_status",
                table: "products",
                column: "processing_status");

            migrationBuilder.CreateIndex(
                name: "ix_products_seller_id",
                table: "products",
                column: "seller_id");

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
                column: "created_at",
                descending: new bool[0]);

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
                name: "ix_seo_scores_listing_content_id",
                table: "seo_scores",
                column: "listing_content_id");

            migrationBuilder.CreateIndex(
                name: "ix_seo_scores_overall_seo_score",
                table: "seo_scores",
                column: "overall_seo_score",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_seo_scores_product_id",
                table: "seo_scores",
                column: "product_id");

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
                name: "ix_support_tickets_assigned_to_admin_id",
                table: "support_tickets",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_created_at",
                table: "support_tickets",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_priority",
                table: "support_tickets",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_seller_id",
                table: "support_tickets",
                column: "seller_id");

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
                name: "ix_system_configurations_config_key",
                table: "system_configurations",
                column: "config_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_system_configurations_last_modified_by_admin_id",
                table: "system_configurations",
                column: "last_modified_by");

            migrationBuilder.CreateIndex(
                name: "ix_system_metrics_metric_type_metric_timestamp",
                table: "system_metrics",
                columns: new[] { "metric_type", "metric_timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_replies_author_id",
                table: "ticket_replies",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_replies_support_ticket_id",
                table: "ticket_replies",
                column: "support_ticket_id");

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
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "batch_job_logs");

            migrationBuilder.DropTable(
                name: "etsy_upload_logs");

            migrationBuilder.DropTable(
                name: "export_packages");

            migrationBuilder.DropTable(
                name: "listing_descriptions");

            migrationBuilder.DropTable(
                name: "listing_generation_history");

            migrationBuilder.DropTable(
                name: "listing_tags");

            migrationBuilder.DropTable(
                name: "listing_titles");

            migrationBuilder.DropTable(
                name: "mockup_images");

            migrationBuilder.DropTable(
                name: "notification_alerts");

            migrationBuilder.DropTable(
                name: "printify_upload_logs");

            migrationBuilder.DropTable(
                name: "seo_scores");

            migrationBuilder.DropTable(
                name: "social_media_shares");

            migrationBuilder.DropTable(
                name: "system_configurations");

            migrationBuilder.DropTable(
                name: "system_metrics");

            migrationBuilder.DropTable(
                name: "ticket_replies");

            migrationBuilder.DropTable(
                name: "batch_job_products");

            migrationBuilder.DropTable(
                name: "etsy_integrations");

            migrationBuilder.DropTable(
                name: "design_images");

            migrationBuilder.DropTable(
                name: "printify_integrations");

            migrationBuilder.DropTable(
                name: "listing_contents");

            migrationBuilder.DropTable(
                name: "promo_videos");

            migrationBuilder.DropTable(
                name: "support_tickets");

            migrationBuilder.DropTable(
                name: "ai_prompts");

            migrationBuilder.DropTable(
                name: "music_tracks");

            migrationBuilder.DropTable(
                name: "video_templates");

            migrationBuilder.DropTable(
                name: "admins");

            migrationBuilder.DropTable(
                name: "design_templates");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "batch_jobs");
        }
    }
}
