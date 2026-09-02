using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeEntityStringLengths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    length_limit record;
                    actual_length integer;
                BEGIN
                    FOR length_limit IN
                        SELECT *
                        FROM (VALUES
                            ('video_templates', 'type', 64, false),
                            ('video_templates', 'resolution', 24, false),
                            ('video_templates', 'platform', 32, false),
                            ('video_templates', 'name', 150, false),
                            ('video_templates', 'aspect_ratio', 16, false),
                            ('ticket_replies', 'author_role', 24, false),
                            ('system_metrics', 'metric_type', 64, false),
                            ('system_configurations', 'config_key', 150, false),
                            ('support_tickets', 'ticket_number', 32, false),
                            ('support_tickets', 'subject', 180, false),
                            ('support_tickets', 'status', 24, false),
                            ('support_tickets', 'priority', 16, false),
                            ('support_tickets', 'category', 64, false),
                            ('subscriptions', 'status', 24, false),
                            ('subscriptions', 'billing_cycle', 16, false),
                            ('subscription_plans', 'tier', 32, false),
                            ('subscription_plans', 'name', 80, false),
                            ('social_media_shares', 'share_status', 24, false),
                            ('social_media_shares', 'platform', 32, false),
                            ('social_media_shares', 'external_post_id', 128, false),
                            ('social_media_shares', 'hashtags', 100, true),
                            ('sellers', 'user_name', 254, false),
                            ('sellers', 'security_stamp', 64, false),
                            ('sellers', 'phone_number', 32, false),
                            ('sellers', 'concurrency_stamp', 64, false),
                            ('sellers', 'oauth_provider', 32, false),
                            ('sellers', 'normalized_user_name', 254, false),
                            ('sellers', 'normalized_email', 254, false),
                            ('sellers', 'full_name', 150, false),
                            ('sellers', 'email', 254, false),
                            ('sellers', 'account_status', 16, false),
                            ('seller_profiles', 'theme_preference', 24, false),
                            ('seller_profiles', 'shop_name', 150, false),
                            ('refresh_tokens', 'revoked_by_ip', 45, false),
                            ('refresh_tokens', 'created_by_ip', 45, false),
                            ('promo_videos', 'video_resolution', 24, false),
                            ('promo_videos', 'text_overlay_font', 64, false),
                            ('promo_videos', 'status', 24, false),
                            ('promo_videos', 'platform_target', 32, false),
                            ('promo_videos', 'aspect_ratio', 16, false),
                            ('promo_videos', 'approval_status', 24, false),
                            ('products', 'product_type', 32, false),
                            ('products', 'processing_status', 24, false),
                            ('products', 'niche_category', 100, false),
                            ('products', 'name', 150, false),
                            ('printify_upload_logs', 'upload_status', 24, false),
                            ('printify_upload_logs', 'sync_type', 32, false),
                            ('printify_upload_logs', 'printify_product_id', 128, false),
                            ('printify_integrations', 'shop_title', 150, false),
                            ('printify_integrations', 'shop_name', 150, false),
                            ('printify_integrations', 'printify_store_id', 128, false),
                            ('payment_methods', 'stripe_payment_method_id', 128, false),
                            ('payment_methods', 'payment_type', 24, false),
                            ('payment_methods', 'card_brand', 32, false),
                            ('notification_alerts', 'type', 64, false),
                            ('notification_alerts', 'title', 150, false),
                            ('notification_alerts', 'severity', 16, false),
                            ('notification_alerts', 'notification_channels', 32, true),
                            ('music_tracks', 'name', 150, false),
                            ('music_tracks', 'mood', 64, false),
                            ('music_tracks', 'license_type', 64, false),
                            ('music_tracks', 'genre', 64, false),
                            ('music_tracks', 'artist_name', 150, false),
                            ('mockup_images', 'mockup_template_type', 64, false),
                            ('listing_tags', 'user_edited_version', 20, true),
                            ('listing_tags', 'generated_tags', 20, true),
                            ('listing_generation_history', 'user_action', 32, false),
                            ('listing_generation_history', 'tags_generated', 20, true),
                            ('listing_contents', 'model_version', 32, false),
                            ('listing_contents', 'approval_status', 24, false),
                            ('listing_contents', 'ai_model_used', 80, false),
                            ('invoices', 'stripe_invoice_id', 128, false),
                            ('invoices', 'status', 24, false),
                            ('invoices', 'invoice_number', 40, false),
                            ('export_packages', 'type', 32, false),
                            ('export_packages', 'status', 24, false),
                            ('export_packages', 'name', 150, false),
                            ('etsy_upload_logs', 'upload_status', 24, false),
                            ('etsy_upload_logs', 'etsy_listing_id', 128, false),
                            ('etsy_integrations', 'shop_name', 150, false),
                            ('etsy_integrations', 'etsy_shop_id', 128, false),
                            ('design_templates', 'type', 32, false),
                            ('design_templates', 'niche_category', 100, false),
                            ('design_templates', 'name', 150, false),
                            ('design_templates', 'art_style', 64, false),
                            ('design_images', 'image_generator_model', 80, false),
                            ('design_images', 'approval_status', 24, false),
                            ('design_images', 'api_response_id', 128, false),
                            ('batch_jobs', 'status', 24, false),
                            ('batch_jobs', 'source_file_type', 16, false),
                            ('batch_jobs', 'priority', 16, false),
                            ('batch_jobs', 'name', 150, false),
                            ('batch_job_products', 'status', 24, false),
                            ('batch_job_logs', 'log_level', 16, false),
                            ('batch_job_logs', 'event_type', 64, false),
                            ('batch_job_logs', 'api_call_identifier', 128, false),
                            ('audit_logs', 'resource_type', 64, false),
                            ('audit_logs', 'action_type', 64, false),
                            ('api_keys', 'service_provider', 32, false),
                            ('api_keys', 'key_identifier', 64, false),
                            ('admins', 'role', 64, false),
                            ('admins', 'full_name', 150, false),
                            ('admins', 'email', 254, false)
                        ) AS limits(table_name, column_name, max_length, is_array)
                    LOOP
                        IF length_limit.is_array THEN
                            EXECUTE format(
                                'SELECT max(char_length(element)) FROM %I CROSS JOIN LATERAL unnest(%I) AS elements(element)',
                                length_limit.table_name,
                                length_limit.column_name)
                            INTO actual_length;
                        ELSE
                            EXECUTE format(
                                'SELECT max(char_length(%I)) FROM %I',
                                length_limit.column_name,
                                length_limit.table_name)
                            INTO actual_length;
                        END IF;

                        IF actual_length > length_limit.max_length THEN
                            RAISE EXCEPTION
                                'Cannot reduce %.% to % characters because an existing value has % characters.',
                                length_limit.table_name,
                                length_limit.column_name,
                                length_limit.max_length,
                                actual_length;
                        END IF;
                    END LOOP;
                END
                $$;
                """);


            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "video_templates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "resolution",
                table: "video_templates",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "platform",
                table: "video_templates",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "video_templates",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "aspect_ratio",
                table: "video_templates",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "author_role",
                table: "ticket_replies",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "metric_type",
                table: "system_metrics",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "config_key",
                table: "system_configurations",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "ticket_number",
                table: "support_tickets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "subject",
                table: "support_tickets",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "support_tickets",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "open",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "open");

            migrationBuilder.AlterColumn<string>(
                name: "priority",
                table: "support_tickets",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "normal",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "normal");

            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "support_tickets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "subscriptions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "active",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "active");

            migrationBuilder.AlterColumn<string>(
                name: "billing_cycle",
                table: "subscriptions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "tier",
                table: "subscription_plans",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "subscription_plans",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "share_status",
                table: "social_media_shares",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "platform",
                table: "social_media_shares",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string[]>(
                name: "hashtags",
                table: "social_media_shares",
                type: "character varying(100)[]",
                nullable: true,
                oldClrType: typeof(string[]),
                oldType: "text[]",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "external_post_id",
                table: "social_media_shares",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "user_name",
                table: "sellers",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "timezone",
                table: "sellers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "UTC",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "UTC");

            migrationBuilder.AlterColumn<string>(
                name: "security_stamp",
                table: "sellers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                table: "sellers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "oauth_provider",
                table: "sellers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "normalized_user_name",
                table: "sellers",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "normalized_email",
                table: "sellers",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "language",
                table: "sellers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "en",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "en");

            migrationBuilder.AlterColumn<string>(
                name: "full_name",
                table: "sellers",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "sellers",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "concurrency_stamp",
                table: "sellers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "account_status",
                table: "sellers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "active",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "active");

            migrationBuilder.AlterColumn<string>(
                name: "theme_preference",
                table: "seller_profiles",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "light",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "light");

            migrationBuilder.AlterColumn<string>(
                name: "shop_name",
                table: "seller_profiles",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "default_timezone",
                table: "seller_profiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "UTC",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "UTC");

            migrationBuilder.AlterColumn<string>(
                name: "default_language",
                table: "seller_profiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "en",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldDefaultValue: "en");

            migrationBuilder.AlterColumn<string>(
                name: "revoked_by_ip",
                table: "refresh_tokens",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "created_by_ip",
                table: "refresh_tokens",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "video_resolution",
                table: "promo_videos",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "text_overlay_font",
                table: "promo_videos",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "promo_videos",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "platform_target",
                table: "promo_videos",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "file_format",
                table: "promo_videos",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "aspect_ratio",
                table: "promo_videos",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "approval_status",
                table: "promo_videos",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "product_type",
                table: "products",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "processing_status",
                table: "products",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "niche_category",
                table: "products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "products",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "upload_status",
                table: "printify_upload_logs",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "sync_type",
                table: "printify_upload_logs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "printify_product_id",
                table: "printify_upload_logs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "shop_title",
                table: "printify_integrations",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "shop_name",
                table: "printify_integrations",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "printify_store_id",
                table: "printify_integrations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "stripe_payment_method_id",
                table: "payment_methods",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "payment_type",
                table: "payment_methods",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "card_brand",
                table: "payment_methods",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "notification_alerts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "notification_alerts",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "severity",
                table: "notification_alerts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string[]>(
                name: "notification_channels",
                table: "notification_alerts",
                type: "character varying(32)[]",
                nullable: false,
                defaultValueSql: "ARRAY['in_app']::character varying(32)[]",
                oldClrType: typeof(string[]),
                oldType: "character varying(50)[]",
                oldDefaultValueSql: "ARRAY['in_app']::character varying(50)[]");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "music_tracks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "mood",
                table: "music_tracks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "license_type",
                table: "music_tracks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "genre",
                table: "music_tracks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "artist_name",
                table: "music_tracks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "mockup_template_type",
                table: "mockup_images",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string[]>(
                name: "user_edited_version",
                table: "listing_tags",
                type: "character varying(20)[]",
                nullable: true,
                oldClrType: typeof(string[]),
                oldType: "text[]",
                oldNullable: true);

            migrationBuilder.AlterColumn<string[]>(
                name: "generated_tags",
                table: "listing_tags",
                type: "character varying(20)[]",
                nullable: false,
                oldClrType: typeof(string[]),
                oldType: "text[]");

            migrationBuilder.AlterColumn<string>(
                name: "user_action",
                table: "listing_generation_history",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string[]>(
                name: "tags_generated",
                table: "listing_generation_history",
                type: "character varying(20)[]",
                nullable: true,
                oldClrType: typeof(string[]),
                oldType: "text[]",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "model_version",
                table: "listing_contents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "approval_status",
                table: "listing_contents",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "ai_model_used",
                table: "listing_contents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "stripe_invoice_id",
                table: "invoices",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "invoices",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "draft",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "draft");

            migrationBuilder.AlterColumn<string>(
                name: "invoice_number",
                table: "invoices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "export_packages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "export_packages",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "preparing",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "preparing");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "export_packages",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "upload_status",
                table: "etsy_upload_logs",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "etsy_listing_id",
                table: "etsy_upload_logs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "shop_name",
                table: "etsy_integrations",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "etsy_shop_id",
                table: "etsy_integrations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "design_templates",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "niche_category",
                table: "design_templates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "design_templates",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "art_style",
                table: "design_templates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "image_generator_model",
                table: "design_images",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "file_format",
                table: "design_images",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "approval_status",
                table: "design_images",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "api_response_id",
                table: "design_images",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "batch_jobs",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "draft",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "draft");

            migrationBuilder.AlterColumn<string>(
                name: "source_file_type",
                table: "batch_jobs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "priority",
                table: "batch_jobs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "normal",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "normal");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "batch_jobs",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "batch_job_products",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "log_level",
                table: "batch_job_logs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "batch_job_logs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "api_call_identifier",
                table: "batch_job_logs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "resource_type",
                table: "audit_logs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "action_type",
                table: "audit_logs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "service_provider",
                table: "api_keys",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "key_identifier",
                table: "api_keys",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "admins",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "full_name",
                table: "admins",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "admins",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "video_templates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "resolution",
                table: "video_templates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<string>(
                name: "platform",
                table: "video_templates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "video_templates",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "aspect_ratio",
                table: "video_templates",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "author_role",
                table: "ticket_replies",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<string>(
                name: "metric_type",
                table: "system_metrics",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "config_key",
                table: "system_configurations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "ticket_number",
                table: "support_tickets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "subject",
                table: "support_tickets",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(180)",
                oldMaxLength: 180);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "support_tickets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "open",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "open");

            migrationBuilder.AlterColumn<string>(
                name: "priority",
                table: "support_tickets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "normal",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "normal");

            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "support_tickets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "subscriptions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "active",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "active");

            migrationBuilder.AlterColumn<string>(
                name: "billing_cycle",
                table: "subscriptions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "tier",
                table: "subscription_plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "subscription_plans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "share_status",
                table: "social_media_shares",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "platform",
                table: "social_media_shares",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string[]>(
                name: "hashtags",
                table: "social_media_shares",
                type: "text[]",
                nullable: true,
                oldClrType: typeof(string[]),
                oldType: "character varying(100)[]",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "external_post_id",
                table: "social_media_shares",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "user_name",
                table: "sellers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);

            migrationBuilder.AlterColumn<string>(
                name: "timezone",
                table: "sellers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "UTC",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "UTC");

            migrationBuilder.AlterColumn<string>(
                name: "security_stamp",
                table: "sellers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone_number",
                table: "sellers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "oauth_provider",
                table: "sellers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "normalized_user_name",
                table: "sellers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);

            migrationBuilder.AlterColumn<string>(
                name: "normalized_email",
                table: "sellers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);

            migrationBuilder.AlterColumn<string>(
                name: "language",
                table: "sellers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "en",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "en");

            migrationBuilder.AlterColumn<string>(
                name: "full_name",
                table: "sellers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "sellers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);

            migrationBuilder.AlterColumn<string>(
                name: "concurrency_stamp",
                table: "sellers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "account_status",
                table: "sellers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "active",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "active");

            migrationBuilder.AlterColumn<string>(
                name: "theme_preference",
                table: "seller_profiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "light",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "light");

            migrationBuilder.AlterColumn<string>(
                name: "shop_name",
                table: "seller_profiles",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "default_timezone",
                table: "seller_profiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "UTC",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "UTC");

            migrationBuilder.AlterColumn<string>(
                name: "default_language",
                table: "seller_profiles",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "en",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "en");

            migrationBuilder.AlterColumn<string>(
                name: "revoked_by_ip",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "created_by_ip",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "video_resolution",
                table: "promo_videos",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<string>(
                name: "text_overlay_font",
                table: "promo_videos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "promo_videos",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "platform_target",
                table: "promo_videos",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "file_format",
                table: "promo_videos",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "aspect_ratio",
                table: "promo_videos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "approval_status",
                table: "promo_videos",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "product_type",
                table: "products",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "processing_status",
                table: "products",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "niche_category",
                table: "products",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "products",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "upload_status",
                table: "printify_upload_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<string>(
                name: "sync_type",
                table: "printify_upload_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "printify_product_id",
                table: "printify_upload_logs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "shop_title",
                table: "printify_integrations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "shop_name",
                table: "printify_integrations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "printify_store_id",
                table: "printify_integrations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "stripe_payment_method_id",
                table: "payment_methods",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "payment_type",
                table: "payment_methods",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<string>(
                name: "card_brand",
                table: "payment_methods",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "notification_alerts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "notification_alerts",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "severity",
                table: "notification_alerts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string[]>(
                name: "notification_channels",
                table: "notification_alerts",
                type: "character varying(50)[]",
                nullable: false,
                defaultValueSql: "ARRAY['in_app']::character varying(50)[]",
                oldClrType: typeof(string[]),
                oldType: "character varying(32)[]",
                oldDefaultValueSql: "ARRAY['in_app']::character varying(32)[]");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "music_tracks",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "mood",
                table: "music_tracks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "license_type",
                table: "music_tracks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "genre",
                table: "music_tracks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "artist_name",
                table: "music_tracks",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "mockup_template_type",
                table: "mockup_images",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string[]>(
                name: "user_edited_version",
                table: "listing_tags",
                type: "text[]",
                nullable: true,
                oldClrType: typeof(string[]),
                oldType: "character varying(20)[]",
                oldNullable: true);

            migrationBuilder.AlterColumn<string[]>(
                name: "generated_tags",
                table: "listing_tags",
                type: "text[]",
                nullable: false,
                oldClrType: typeof(string[]),
                oldType: "character varying(20)[]");

            migrationBuilder.AlterColumn<string>(
                name: "user_action",
                table: "listing_generation_history",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string[]>(
                name: "tags_generated",
                table: "listing_generation_history",
                type: "text[]",
                nullable: true,
                oldClrType: typeof(string[]),
                oldType: "character varying(20)[]",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "model_version",
                table: "listing_contents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "approval_status",
                table: "listing_contents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "ai_model_used",
                table: "listing_contents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "stripe_invoice_id",
                table: "invoices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "invoices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "draft",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "draft");

            migrationBuilder.AlterColumn<string>(
                name: "invoice_number",
                table: "invoices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "export_packages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "export_packages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "preparing",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "preparing");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "export_packages",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "upload_status",
                table: "etsy_upload_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24);

            migrationBuilder.AlterColumn<string>(
                name: "etsy_listing_id",
                table: "etsy_upload_logs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "shop_name",
                table: "etsy_integrations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "etsy_shop_id",
                table: "etsy_integrations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "design_templates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "niche_category",
                table: "design_templates",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "design_templates",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "art_style",
                table: "design_templates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "image_generator_model",
                table: "design_images",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "file_format",
                table: "design_images",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "approval_status",
                table: "design_images",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "api_response_id",
                table: "design_images",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "batch_jobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "draft",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "draft");

            migrationBuilder.AlterColumn<string>(
                name: "source_file_type",
                table: "batch_jobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "priority",
                table: "batch_jobs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "normal",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldDefaultValue: "normal");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "batch_jobs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "batch_job_products",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "pending",
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldDefaultValue: "pending");

            migrationBuilder.AlterColumn<string>(
                name: "log_level",
                table: "batch_job_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "batch_job_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "api_call_identifier",
                table: "batch_job_logs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "resource_type",
                table: "audit_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "action_type",
                table: "audit_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "service_provider",
                table: "api_keys",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "key_identifier",
                table: "api_keys",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "admins",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "full_name",
                table: "admins",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "admins",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);
        }
    }
}
