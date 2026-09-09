using System;
using System.Collections.Generic;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.Persistence;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AiPrompt> AiPrompts { get; set; }

    public virtual DbSet<ApiKey> ApiKeys { get; set; }

    public virtual DbSet<ApiUsageRecord> ApiUsageRecords { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<AuthToken> AuthTokens { get; set; }

    public virtual DbSet<BatchJob> BatchJobs { get; set; }

    public virtual DbSet<BatchJobLog> BatchJobLogs { get; set; }

    public virtual DbSet<BatchJobProduct> BatchJobProducts { get; set; }

    public virtual DbSet<DesignImage> DesignImages { get; set; }

    public virtual DbSet<DesignTemplate> DesignTemplates { get; set; }

    public virtual DbSet<EtsyIntegration> EtsyIntegrations { get; set; }

    public virtual DbSet<EtsyUploadLog> EtsyUploadLogs { get; set; }

    public virtual DbSet<ExportPackage> ExportPackages { get; set; }

    public virtual DbSet<ExportPackageItem> ExportPackageItems { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<ListingContent> ListingContents { get; set; }

    public virtual DbSet<ListingDescription> ListingDescriptions { get; set; }

    public virtual DbSet<ListingGenerationHistory> ListingGenerationHistories { get; set; }

    public virtual DbSet<ListingTag> ListingTags { get; set; }

    public virtual DbSet<ListingTagItem> ListingTagItems { get; set; }

    public virtual DbSet<ListingTitle> ListingTitles { get; set; }

    public virtual DbSet<MockupImage> MockupImages { get; set; }

    public virtual DbSet<MockupTemplate> MockupTemplates { get; set; }

    public virtual DbSet<MusicTrack> MusicTracks { get; set; }

    public virtual DbSet<NotificationAlert> NotificationAlerts { get; set; }

    public virtual DbSet<NotificationDelivery> NotificationDeliveries { get; set; }

    public virtual DbSet<PaymentMethod> PaymentMethods { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<PlanFeature> PlanFeatures { get; set; }

    public virtual DbSet<PrintifyIntegration> PrintifyIntegrations { get; set; }

    public virtual DbSet<PrintifyUploadLog> PrintifyUploadLogs { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<ProductMockupTemplate> ProductMockupTemplates { get; set; }

    public virtual DbSet<PromoVideo> PromoVideos { get; set; }

    public virtual DbSet<PromoVideoScene> PromoVideoScenes { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<RolePermission> RolePermissions { get; set; }

    public virtual DbSet<SeoScore> SeoScores { get; set; }

    public virtual DbSet<ShareHashtag> ShareHashtags { get; set; }

    public virtual DbSet<SocialMediaShare> SocialMediaShares { get; set; }

    public virtual DbSet<Subscription> Subscriptions { get; set; }

    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

    public virtual DbSet<SupportTicket> SupportTickets { get; set; }

    public virtual DbSet<TicketAttachment> TicketAttachments { get; set; }

    public virtual DbSet<TicketReply> TicketReplies { get; set; }

    public virtual DbSet<UsageStatistic> UsageStatistics { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserProfile> UserProfiles { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<VideoTemplate> VideoTemplates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<AiPrompt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ai_prompts_pkey");

            entity.ToTable("ai_prompts");

            entity.HasIndex(e => e.ProductId, "idx_ai_prompts_product_id");

            entity.HasIndex(e => new { e.ProductId, e.VersionNumber }, "uq_ai_prompts_product_version").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DesignTemplateId).HasColumnName("design_template_id");
            entity.Property(e => e.FewShotExamples)
                .HasColumnType("jsonb")
                .HasColumnName("few_shot_examples");
            entity.Property(e => e.GeneratedPrompt).HasColumnName("generated_prompt");
            entity.Property(e => e.IsApprovedByUser)
                .HasDefaultValue(false)
                .HasColumnName("is_approved_by_user");
            entity.Property(e => e.ModifiedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("modified_at");
            entity.Property(e => e.OriginalDescription).HasColumnName("original_description");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.SystemPrompt).HasColumnName("system_prompt");
            entity.Property(e => e.UserNotes).HasColumnName("user_notes");
            entity.Property(e => e.VersionNumber)
                .HasDefaultValue(1)
                .HasColumnName("version_number");

            entity.HasOne(d => d.DesignTemplate).WithMany(p => p.AiPrompts)
                .HasForeignKey(d => d.DesignTemplateId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("ai_prompts_design_template_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.AiPrompts)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("ai_prompts_product_id_fkey");
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("api_keys_pkey");

            entity.ToTable("api_keys");

            entity.HasIndex(e => e.ServiceProvider, "idx_api_keys_service_provider");

            entity.HasIndex(e => e.UserId, "idx_api_keys_user_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByIp)
                .HasMaxLength(45)
                .HasColumnName("created_by_ip");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.KeyIdentifier)
                .HasMaxLength(100)
                .HasColumnName("key_identifier");
            entity.Property(e => e.KeyLast4)
                .HasMaxLength(4)
                .HasColumnName("key_last_4");
            entity.Property(e => e.KeyValueEncrypted).HasColumnName("key_value_encrypted");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(e => e.ServiceProvider)
                .HasMaxLength(50)
                .HasColumnName("service_provider");
            entity.Property(e => e.UsageCount)
                .HasDefaultValue(0)
                .HasColumnName("usage_count");
            entity.Property(e => e.UsageLimit).HasColumnName("usage_limit");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.ApiKeys)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("api_keys_user_id_fkey");
        });

        modelBuilder.Entity<ApiUsageRecord>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("api_usage_records_pkey");

            entity.ToTable("api_usage_records");

            entity.HasIndex(e => e.BatchJobId, "idx_api_usage_batch_job_id");

            entity.HasIndex(e => new { e.Provider, e.Feature }, "idx_api_usage_provider_feature");

            entity.HasIndex(e => e.ProviderRequestId, "idx_api_usage_provider_request_id");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "idx_api_usage_user_created");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.BatchJobId).HasColumnName("batch_job_id");
            entity.Property(e => e.CostUsd)
                .HasPrecision(12, 6)
                .HasColumnName("cost_usd");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ErrorCode)
                .HasMaxLength(100)
                .HasColumnName("error_code");
            entity.Property(e => e.Feature)
                .HasMaxLength(50)
                .HasColumnName("feature");
            entity.Property(e => e.LatencyMs).HasColumnName("latency_ms");
            entity.Property(e => e.ModelName)
                .HasMaxLength(100)
                .HasColumnName("model_name");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Provider)
                .HasMaxLength(50)
                .HasColumnName("provider");
            entity.Property(e => e.ProviderRequestId)
                .HasMaxLength(255)
                .HasColumnName("provider_request_id");
            entity.Property(e => e.RequestUnits)
                .HasDefaultValue(1)
                .HasColumnName("request_units");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasColumnName("status");
            entity.Property(e => e.TokensInput).HasColumnName("tokens_input");
            entity.Property(e => e.TokensOutput).HasColumnName("tokens_output");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.BatchJob).WithMany(p => p.ApiUsageRecords)
                .HasForeignKey(d => d.BatchJobId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("api_usage_records_batch_job_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.ApiUsageRecords)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("api_usage_records_product_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.ApiUsageRecords)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("api_usage_records_user_id_fkey");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("audit_logs_pkey");

            entity.ToTable("audit_logs");

            entity.HasIndex(e => e.ActorUserId, "idx_audit_logs_actor_user_id");

            entity.HasIndex(e => e.CreatedAt, "idx_audit_logs_created_at");

            entity.HasIndex(e => new { e.ResourceType, e.ResourceId }, "idx_audit_logs_resource");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ActionType)
                .HasMaxLength(100)
                .HasColumnName("action_type");
            entity.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address");
            entity.Property(e => e.NewValue)
                .HasColumnType("jsonb")
                .HasColumnName("new_value");
            entity.Property(e => e.OldValue)
                .HasColumnType("jsonb")
                .HasColumnName("old_value");
            entity.Property(e => e.ResourceId).HasColumnName("resource_id");
            entity.Property(e => e.ResourceType)
                .HasMaxLength(100)
                .HasColumnName("resource_type");
            entity.Property(e => e.UserAgent).HasColumnName("user_agent");

            entity.HasOne(d => d.ActorUser).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("audit_logs_actor_user_id_fkey");
        });

        modelBuilder.Entity<AuthToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("auth_tokens_pkey");

            entity.ToTable("auth_tokens");

            entity.HasIndex(e => e.TokenHash, "auth_tokens_token_hash_key").IsUnique();

            entity.HasIndex(e => e.ExpiresAt, "idx_auth_tokens_expires_at");

            entity.HasIndex(e => new { e.UserId, e.TokenType }, "idx_auth_tokens_user_type");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByIp)
                .HasMaxLength(45)
                .HasColumnName("created_by_ip");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.Property(e => e.TokenHash)
                .HasMaxLength(255)
                .HasColumnName("token_hash");
            entity.Property(e => e.TokenType)
                .HasMaxLength(50)
                .HasColumnName("token_type");
            entity.Property(e => e.UsedAt).HasColumnName("used_at");
            entity.Property(e => e.UserAgent).HasColumnName("user_agent");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.AuthTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("auth_tokens_user_id_fkey");
        });

        modelBuilder.Entity<BatchJob>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("batch_jobs_pkey");

            entity.ToTable("batch_jobs");

            entity.HasIndex(e => e.CreatedAt, "idx_batch_jobs_created_at");

            entity.HasIndex(e => e.StartedAt, "idx_batch_jobs_started_at");

            entity.HasIndex(e => e.Status, "idx_batch_jobs_status");

            entity.HasIndex(e => e.UserId, "idx_batch_jobs_user_id");

            entity.HasIndex(e => new { e.UserId, e.Status }, "idx_batch_jobs_user_status");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ActualCostUsd)
                .HasPrecision(12, 6)
                .HasDefaultValueSql("0")
                .HasColumnName("actual_cost_usd");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.Config)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("config");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EstimatedCompletionTime).HasColumnName("estimated_completion_time");
            entity.Property(e => e.EstimatedCostUsd)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("estimated_cost_usd");
            entity.Property(e => e.FailedProducts)
                .HasDefaultValue(0)
                .HasColumnName("failed_products");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.Priority)
                .HasMaxLength(50)
                .HasDefaultValueSql("'normal'::character varying")
                .HasColumnName("priority");
            entity.Property(e => e.ProcessedProducts)
                .HasDefaultValue(0)
                .HasColumnName("processed_products");
            entity.Property(e => e.ProgressPercentage)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("progress_percentage");
            entity.Property(e => e.SkippedProducts)
                .HasDefaultValue(0)
                .HasColumnName("skipped_products");
            entity.Property(e => e.SourceFileHash)
                .HasMaxLength(64)
                .HasColumnName("source_file_hash");
            entity.Property(e => e.SourceFileType)
                .HasMaxLength(50)
                .HasColumnName("source_file_type");
            entity.Property(e => e.SourceFileUrl).HasColumnName("source_file_url");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'draft'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TotalProducts)
                .HasDefaultValue(0)
                .HasColumnName("total_products");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.BatchJobs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("batch_jobs_user_id_fkey");
        });

        modelBuilder.Entity<BatchJobLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("batch_job_logs_pkey");

            entity.ToTable("batch_job_logs");

            entity.HasIndex(e => e.BatchJobId, "idx_batch_job_logs_batch_job_id");

            entity.HasIndex(e => e.CreatedAt, "idx_batch_job_logs_created_at");

            entity.HasIndex(e => e.LogLevel, "idx_batch_job_logs_log_level");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ApiUsageRecordId).HasColumnName("api_usage_record_id");
            entity.Property(e => e.BatchJobId).HasColumnName("batch_job_id");
            entity.Property(e => e.BatchJobProductId).HasColumnName("batch_job_product_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Details)
                .HasColumnType("jsonb")
                .HasColumnName("details");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.EventType)
                .HasMaxLength(100)
                .HasColumnName("event_type");
            entity.Property(e => e.LogLevel)
                .HasMaxLength(50)
                .HasColumnName("log_level");
            entity.Property(e => e.Message).HasColumnName("message");

            entity.HasOne(d => d.ApiUsageRecord).WithMany(p => p.BatchJobLogs)
                .HasForeignKey(d => d.ApiUsageRecordId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("batch_job_logs_api_usage_record_id_fkey");

            entity.HasOne(d => d.BatchJob).WithMany(p => p.BatchJobLogs)
                .HasForeignKey(d => d.BatchJobId)
                .HasConstraintName("batch_job_logs_batch_job_id_fkey");

            entity.HasOne(d => d.BatchJobProduct).WithMany(p => p.BatchJobLogs)
                .HasForeignKey(d => d.BatchJobProductId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("batch_job_logs_batch_job_product_id_fkey");
        });

        modelBuilder.Entity<BatchJobProduct>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("batch_job_products_pkey");

            entity.ToTable("batch_job_products");

            entity.HasIndex(e => e.BatchJobId, "idx_batch_job_products_batch_job_id");

            entity.HasIndex(e => new { e.BatchJobId, e.Status }, "idx_batch_job_products_batch_status");

            entity.HasIndex(e => e.ProductId, "idx_batch_job_products_product_id");

            entity.HasIndex(e => e.Status, "idx_batch_job_products_status");

            entity.HasIndex(e => new { e.BatchJobId, e.SequenceOrder }, "uq_batch_job_product_order").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.BatchJobId).HasColumnName("batch_job_id");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CurrentStep)
                .HasMaxLength(50)
                .HasColumnName("current_step");
            entity.Property(e => e.DurationSeconds)
                .HasPrecision(10, 2)
                .HasColumnName("duration_seconds");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.RawRowData)
                .HasColumnType("jsonb")
                .HasColumnName("raw_row_data");
            entity.Property(e => e.RetryCount)
                .HasDefaultValue(0)
                .HasColumnName("retry_count");
            entity.Property(e => e.SequenceOrder).HasColumnName("sequence_order");
            entity.Property(e => e.SourceRowIndex).HasColumnName("source_row_index");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.BatchJob).WithMany(p => p.BatchJobProducts)
                .HasForeignKey(d => d.BatchJobId)
                .HasConstraintName("batch_job_products_batch_job_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.BatchJobProducts)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("batch_job_products_product_id_fkey");
        });

        modelBuilder.Entity<DesignImage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("design_images_pkey");

            entity.ToTable("design_images");

            entity.HasIndex(e => e.ApprovalStatus, "idx_design_images_approval_status");

            entity.HasIndex(e => e.CreatedAt, "idx_design_images_created_at");

            entity.HasIndex(e => e.IsFinal, "idx_design_images_is_final");

            entity.HasIndex(e => e.ProductId, "idx_design_images_product_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AiPromptId).HasColumnName("ai_prompt_id");
            entity.Property(e => e.ApiUsageRecordId).HasColumnName("api_usage_record_id");
            entity.Property(e => e.ApprovalStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("approval_status");
            entity.Property(e => e.BatchJobId).HasColumnName("batch_job_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.FileFormat)
                .HasMaxLength(10)
                .HasColumnName("file_format");
            entity.Property(e => e.FileSizeMb)
                .HasPrecision(10, 2)
                .HasColumnName("file_size_mb");
            entity.Property(e => e.GenerationMetadata)
                .HasColumnType("jsonb")
                .HasColumnName("generation_metadata");
            entity.Property(e => e.GenerationTimeSeconds)
                .HasPrecision(10, 3)
                .HasColumnName("generation_time_seconds");
            entity.Property(e => e.ImageGeneratorModel)
                .HasMaxLength(100)
                .HasColumnName("image_generator_model");
            entity.Property(e => e.ImageHeightPx).HasColumnName("image_height_px");
            entity.Property(e => e.ImageUrl).HasColumnName("image_url");
            entity.Property(e => e.ImageWidthPx).HasColumnName("image_width_px");
            entity.Property(e => e.IsFinal)
                .HasDefaultValue(false)
                .HasColumnName("is_final");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.QualityScore)
                .HasPrecision(3, 2)
                .HasColumnName("quality_score");
            entity.Property(e => e.StorageKey)
                .HasMaxLength(500)
                .HasColumnName("storage_key");
            entity.Property(e => e.StorageProvider)
                .HasMaxLength(50)
                .HasDefaultValueSql("'s3'::character varying")
                .HasColumnName("storage_provider");
            entity.Property(e => e.UserRating).HasColumnName("user_rating");
            entity.Property(e => e.VariationIndex)
                .HasDefaultValue(1)
                .HasColumnName("variation_index");

            entity.HasOne(d => d.AiPrompt).WithMany(p => p.DesignImages)
                .HasForeignKey(d => d.AiPromptId)
                .HasConstraintName("design_images_ai_prompt_id_fkey");

            entity.HasOne(d => d.ApiUsageRecord).WithMany(p => p.DesignImages)
                .HasForeignKey(d => d.ApiUsageRecordId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("design_images_api_usage_record_id_fkey");

            entity.HasOne(d => d.BatchJob).WithMany(p => p.DesignImages)
                .HasForeignKey(d => d.BatchJobId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("design_images_batch_job_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.DesignImages)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("design_images_product_id_fkey");
        });

        modelBuilder.Entity<DesignTemplate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("design_templates_pkey");

            entity.ToTable("design_templates");

            entity.HasIndex(e => e.ArtStyle, "idx_design_templates_art_style");

            entity.HasIndex(e => e.Type, "idx_design_templates_type");

            entity.HasIndex(e => e.UserId, "idx_design_templates_user_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ArtStyle)
                .HasMaxLength(100)
                .HasColumnName("art_style");
            entity.Property(e => e.BasePrompt).HasColumnName("base_prompt");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.ExamplePrompts)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("example_prompts");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsSystemTemplate)
                .HasDefaultValue(false)
                .HasColumnName("is_system_template");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.NicheCategory)
                .HasMaxLength(255)
                .HasColumnName("niche_category");
            entity.Property(e => e.PreviewImageUrl).HasColumnName("preview_image_url");
            entity.Property(e => e.StyleDescription).HasColumnName("style_description");
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .HasColumnName("type");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UsageCount)
                .HasDefaultValue(0)
                .HasColumnName("usage_count");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.DesignTemplates)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("design_templates_user_id_fkey");
        });

        modelBuilder.Entity<EtsyIntegration>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("etsy_integrations_pkey");

            entity.ToTable("etsy_integrations");

            entity.HasIndex(e => e.EtsyShopId, "idx_etsy_integrations_shop_id");

            entity.HasIndex(e => e.UserId, "idx_etsy_integrations_user_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.EtsyOauthTokenEncrypted).HasColumnName("etsy_oauth_token_encrypted");
            entity.Property(e => e.EtsyRefreshTokenEncrypted).HasColumnName("etsy_refresh_token_encrypted");
            entity.Property(e => e.EtsyShopId)
                .HasMaxLength(255)
                .HasColumnName("etsy_shop_id");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsDefault)
                .HasDefaultValue(false)
                .HasColumnName("is_default");
            entity.Property(e => e.LastSyncAt).HasColumnName("last_sync_at");
            entity.Property(e => e.OauthExpiresAt).HasColumnName("oauth_expires_at");
            entity.Property(e => e.ShopName)
                .HasMaxLength(255)
                .HasColumnName("shop_name");
            entity.Property(e => e.TotalListingsCreated)
                .HasDefaultValue(0)
                .HasColumnName("total_listings_created");
            entity.Property(e => e.TotalListingsUpdated)
                .HasDefaultValue(0)
                .HasColumnName("total_listings_updated");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.EtsyIntegrations)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("etsy_integrations_user_id_fkey");
        });

        modelBuilder.Entity<EtsyUploadLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("etsy_upload_logs_pkey");

            entity.ToTable("etsy_upload_logs");

            entity.HasIndex(e => e.EtsyIntegrationId, "idx_etsy_upload_logs_integration_id");

            entity.HasIndex(e => e.ProductId, "idx_etsy_upload_logs_product_id");

            entity.HasIndex(e => e.UploadStatus, "idx_etsy_upload_logs_status");

            entity.HasIndex(e => new { e.EtsyIntegrationId, e.IdempotencyKey }, "uq_etsy_idempotency").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ApiResponse)
                .HasColumnType("jsonb")
                .HasColumnName("api_response");
            entity.Property(e => e.AttemptedAt).HasColumnName("attempted_at");
            entity.Property(e => e.BatchJobId).HasColumnName("batch_job_id");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.ConfirmedByUserAt).HasColumnName("confirmed_by_user_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.EtsyIntegrationId).HasColumnName("etsy_integration_id");
            entity.Property(e => e.EtsyListingId)
                .HasMaxLength(255)
                .HasColumnName("etsy_listing_id");
            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(255)
                .HasColumnName("idempotency_key");
            entity.Property(e => e.ListingState)
                .HasMaxLength(50)
                .HasDefaultValueSql("'draft'::character varying")
                .HasColumnName("listing_state");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.PublishImmediately)
                .HasDefaultValue(false)
                .HasColumnName("publish_immediately");
            entity.Property(e => e.RetryCount)
                .HasDefaultValue(0)
                .HasColumnName("retry_count");
            entity.Property(e => e.UploadPayload)
                .HasColumnType("jsonb")
                .HasColumnName("upload_payload");
            entity.Property(e => e.UploadStatus)
                .HasMaxLength(50)
                .HasColumnName("upload_status");

            entity.HasOne(d => d.BatchJob).WithMany(p => p.EtsyUploadLogs)
                .HasForeignKey(d => d.BatchJobId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("etsy_upload_logs_batch_job_id_fkey");

            entity.HasOne(d => d.EtsyIntegration).WithMany(p => p.EtsyUploadLogs)
                .HasForeignKey(d => d.EtsyIntegrationId)
                .HasConstraintName("etsy_upload_logs_etsy_integration_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.EtsyUploadLogs)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("etsy_upload_logs_product_id_fkey");
        });

        modelBuilder.Entity<ExportPackage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("export_packages_pkey");

            entity.ToTable("export_packages");

            entity.HasIndex(e => e.BatchJobId, "idx_export_packages_batch_job_id");

            entity.HasIndex(e => e.ExpiresAt, "idx_export_packages_expires_at");

            entity.HasIndex(e => e.Status, "idx_export_packages_status");

            entity.HasIndex(e => e.UserId, "idx_export_packages_user_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.BatchJobId).HasColumnName("batch_job_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CreationTimeSeconds)
                .HasPrecision(10, 2)
                .HasColumnName("creation_time_seconds");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DownloadUrl).HasColumnName("download_url");
            entity.Property(e => e.DownloadedAt).HasColumnName("downloaded_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.FileSizeMb)
                .HasPrecision(10, 2)
                .HasColumnName("file_size_mb");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'preparing'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.StorageKey)
                .HasMaxLength(500)
                .HasColumnName("storage_key");
            entity.Property(e => e.StorageProvider)
                .HasMaxLength(50)
                .HasDefaultValueSql("'s3'::character varying")
                .HasColumnName("storage_provider");
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .HasColumnName("type");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.BatchJob).WithMany(p => p.ExportPackages)
                .HasForeignKey(d => d.BatchJobId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("export_packages_batch_job_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.ExportPackages)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("export_packages_user_id_fkey");
        });

        modelBuilder.Entity<ExportPackageItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("export_package_items_pkey");

            entity.ToTable("export_package_items");

            entity.HasIndex(e => e.ProductId, "idx_export_package_items_product_id");

            entity.HasIndex(e => e.ItemStatus, "idx_export_package_items_status");

            entity.HasIndex(e => new { e.ExportPackageId, e.ProductId }, "uq_export_package_product").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.ExportPackageId).HasColumnName("export_package_id");
            entity.Property(e => e.FolderPathInZip)
                .HasMaxLength(500)
                .HasColumnName("folder_path_in_zip");
            entity.Property(e => e.IncludeDesignImages)
                .HasDefaultValue(true)
                .HasColumnName("include_design_images");
            entity.Property(e => e.IncludeListingContent)
                .HasDefaultValue(true)
                .HasColumnName("include_listing_content");
            entity.Property(e => e.IncludeMockupImages)
                .HasDefaultValue(true)
                .HasColumnName("include_mockup_images");
            entity.Property(e => e.IncludePromoVideo)
                .HasDefaultValue(true)
                .HasColumnName("include_promo_video");
            entity.Property(e => e.ItemStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("item_status");
            entity.Property(e => e.ProductId).HasColumnName("product_id");

            entity.HasOne(d => d.ExportPackage).WithMany(p => p.ExportPackageItems)
                .HasForeignKey(d => d.ExportPackageId)
                .HasConstraintName("export_package_items_export_package_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.ExportPackageItems)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("export_package_items_product_id_fkey");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("invoices_pkey");

            entity.ToTable("invoices");

            entity.HasIndex(e => e.CreatedAt, "idx_invoices_created_at");

            entity.HasIndex(e => e.Status, "idx_invoices_status");

            entity.HasIndex(e => e.UserId, "idx_invoices_user_id");

            entity.HasIndex(e => e.InvoiceNumber, "invoices_invoice_number_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AmountUsd)
                .HasPrecision(10, 2)
                .HasColumnName("amount_usd");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.InvoiceDate).HasColumnName("invoice_date");
            entity.Property(e => e.InvoiceNumber)
                .HasMaxLength(50)
                .HasColumnName("invoice_number");
            entity.Property(e => e.Items)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("items");
            entity.Property(e => e.PaymentDate).HasColumnName("payment_date");
            entity.Property(e => e.PdfUrl).HasColumnName("pdf_url");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'draft'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.StripeInvoiceId)
                .HasMaxLength(255)
                .HasColumnName("stripe_invoice_id");
            entity.Property(e => e.SubscriptionId).HasColumnName("subscription_id");
            entity.Property(e => e.TaxAmount)
                .HasPrecision(10, 2)
                .HasColumnName("tax_amount");
            entity.Property(e => e.TotalAmount)
                .HasPrecision(10, 2)
                .HasColumnName("total_amount");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Subscription).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.SubscriptionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("invoices_subscription_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("invoices_user_id_fkey");
        });

        modelBuilder.Entity<ListingContent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("listing_contents_pkey");

            entity.ToTable("listing_contents");

            entity.HasIndex(e => e.ApprovalStatus, "idx_listing_contents_approval_status");

            entity.HasIndex(e => e.ProductId, "idx_listing_contents_product_id");

            entity.HasIndex(e => e.ProductId, "listing_contents_product_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AiModelUsed)
                .HasMaxLength(100)
                .HasColumnName("ai_model_used");
            entity.Property(e => e.ApiUsageRecordId).HasColumnName("api_usage_record_id");
            entity.Property(e => e.ApprovalStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("approval_status");
            entity.Property(e => e.ApprovedAt).HasColumnName("approved_at");
            entity.Property(e => e.BatchJobId).HasColumnName("batch_job_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.GenerationTimeSeconds)
                .HasPrecision(10, 2)
                .HasColumnName("generation_time_seconds");
            entity.Property(e => e.ModelVersion)
                .HasMaxLength(50)
                .HasColumnName("model_version");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.ApiUsageRecord).WithMany(p => p.ListingContents)
                .HasForeignKey(d => d.ApiUsageRecordId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("listing_contents_api_usage_record_id_fkey");

            entity.HasOne(d => d.BatchJob).WithMany(p => p.ListingContents)
                .HasForeignKey(d => d.BatchJobId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("listing_contents_batch_job_id_fkey");

            entity.HasOne(d => d.Product).WithOne(p => p.ListingContent)
                .HasForeignKey<ListingContent>(d => d.ProductId)
                .HasConstraintName("listing_contents_product_id_fkey");
        });

        modelBuilder.Entity<ListingDescription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("listing_descriptions_pkey");

            entity.ToTable("listing_descriptions");

            entity.HasIndex(e => new { e.ListingContentId, e.VersionNumber }, "uq_listing_descriptions_version").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AiGeneratedDescription).HasColumnName("ai_generated_description");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CurrentDescription).HasColumnName("current_description");
            entity.Property(e => e.IsUserEdited)
                .HasDefaultValue(false)
                .HasColumnName("is_user_edited");
            entity.Property(e => e.KeywordDensityOptimal).HasColumnName("keyword_density_optimal");
            entity.Property(e => e.ListingContentId).HasColumnName("listing_content_id");
            entity.Property(e => e.ModifiedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("modified_at");
            entity.Property(e => e.SeoScore)
                .HasPrecision(5, 2)
                .HasColumnName("seo_score");
            entity.Property(e => e.StructureFollowed)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("structure_followed");
            entity.Property(e => e.VersionNumber)
                .HasDefaultValue(1)
                .HasColumnName("version_number");
            entity.Property(e => e.WordCount).HasColumnName("word_count");

            entity.HasOne(d => d.ListingContent).WithMany(p => p.ListingDescriptions)
                .HasForeignKey(d => d.ListingContentId)
                .HasConstraintName("listing_descriptions_listing_content_id_fkey");
        });

        modelBuilder.Entity<ListingGenerationHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("listing_generation_history_pkey");

            entity.ToTable("listing_generation_history");

            entity.HasIndex(e => new { e.ListingContentId, e.GenerationNumber }, "uq_listing_generation_number").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AdjustmentHint).HasColumnName("adjustment_hint");
            entity.Property(e => e.ApiUsageRecordId).HasColumnName("api_usage_record_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DescriptionGenerated).HasColumnName("description_generated");
            entity.Property(e => e.FeedbackNotes).HasColumnName("feedback_notes");
            entity.Property(e => e.GenerationNumber).HasColumnName("generation_number");
            entity.Property(e => e.ListingContentId).HasColumnName("listing_content_id");
            entity.Property(e => e.PromptUsed).HasColumnName("prompt_used");
            entity.Property(e => e.RawAiResponse).HasColumnName("raw_ai_response");
            entity.Property(e => e.TagsGenerated).HasColumnName("tags_generated");
            entity.Property(e => e.TitleGenerated)
                .HasMaxLength(140)
                .HasColumnName("title_generated");
            entity.Property(e => e.UserAction)
                .HasMaxLength(100)
                .HasColumnName("user_action");

            entity.HasOne(d => d.ApiUsageRecord).WithMany(p => p.ListingGenerationHistories)
                .HasForeignKey(d => d.ApiUsageRecordId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("listing_generation_history_api_usage_record_id_fkey");

            entity.HasOne(d => d.ListingContent).WithMany(p => p.ListingGenerationHistories)
                .HasForeignKey(d => d.ListingContentId)
                .HasConstraintName("listing_generation_history_listing_content_id_fkey");
        });

        modelBuilder.Entity<ListingTag>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("listing_tags_pkey");

            entity.ToTable("listing_tags");

            entity.HasIndex(e => new { e.ListingContentId, e.VersionNumber }, "uq_listing_tags_version").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsUserEdited)
                .HasDefaultValue(false)
                .HasColumnName("is_user_edited");
            entity.Property(e => e.ListingContentId).HasColumnName("listing_content_id");
            entity.Property(e => e.ModifiedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("modified_at");
            entity.Property(e => e.SeoScore)
                .HasPrecision(5, 2)
                .HasColumnName("seo_score");
            entity.Property(e => e.TagCount).HasColumnName("tag_count");
            entity.Property(e => e.TagTypeDistribution)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("tag_type_distribution");
            entity.Property(e => e.VersionNumber)
                .HasDefaultValue(1)
                .HasColumnName("version_number");

            entity.HasOne(d => d.ListingContent).WithMany(p => p.ListingTags)
                .HasForeignKey(d => d.ListingContentId)
                .HasConstraintName("listing_tags_listing_content_id_fkey");
        });

        modelBuilder.Entity<ListingTagItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("listing_tag_items_pkey");

            entity.ToTable("listing_tag_items");

            entity.HasIndex(e => e.NormalizedValue, "idx_listing_tag_items_normalized");

            entity.HasIndex(e => new { e.ListingTagId, e.NormalizedValue }, "uq_listing_tag_no_duplicate").IsUnique();

            entity.HasIndex(e => new { e.ListingTagId, e.Position }, "uq_listing_tag_position").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ListingTagId).HasColumnName("listing_tag_id");
            entity.Property(e => e.NormalizedValue)
                .HasMaxLength(20)
                .HasColumnName("normalized_value");
            entity.Property(e => e.Position).HasColumnName("position");
            entity.Property(e => e.Source)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ai'::character varying")
                .HasColumnName("source");
            entity.Property(e => e.TagType)
                .HasMaxLength(50)
                .HasColumnName("tag_type");
            entity.Property(e => e.TagValue)
                .HasMaxLength(20)
                .HasColumnName("tag_value");

            entity.HasOne(d => d.ListingTag).WithMany(p => p.ListingTagItems)
                .HasForeignKey(d => d.ListingTagId)
                .HasConstraintName("listing_tag_items_listing_tag_id_fkey");
        });

        modelBuilder.Entity<ListingTitle>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("listing_titles_pkey");

            entity.ToTable("listing_titles");

            entity.HasIndex(e => new { e.ListingContentId, e.VersionNumber }, "uq_listing_titles_version").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AiGeneratedTitle)
                .HasMaxLength(140)
                .HasColumnName("ai_generated_title");
            entity.Property(e => e.CharacterCount).HasColumnName("character_count");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CurrentTitle)
                .HasMaxLength(140)
                .HasColumnName("current_title");
            entity.Property(e => e.IncludesPrimaryKeyword).HasColumnName("includes_primary_keyword");
            entity.Property(e => e.IsUserEdited)
                .HasDefaultValue(false)
                .HasColumnName("is_user_edited");
            entity.Property(e => e.ListingContentId).HasColumnName("listing_content_id");
            entity.Property(e => e.ModifiedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("modified_at");
            entity.Property(e => e.SeoScore)
                .HasPrecision(5, 2)
                .HasColumnName("seo_score");
            entity.Property(e => e.VersionNumber)
                .HasDefaultValue(1)
                .HasColumnName("version_number");

            entity.HasOne(d => d.ListingContent).WithMany(p => p.ListingTitles)
                .HasForeignKey(d => d.ListingContentId)
                .HasConstraintName("listing_titles_listing_content_id_fkey");
        });

        modelBuilder.Entity<MockupImage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("mockup_images_pkey");

            entity.ToTable("mockup_images");

            entity.HasIndex(e => e.DesignImageId, "idx_mockup_images_design_image_id");

            entity.HasIndex(e => e.ProductId, "idx_mockup_images_product_id");

            entity.HasIndex(e => e.MockupTemplateId, "idx_mockup_images_template_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ApiUsageRecordId).HasColumnName("api_usage_record_id");
            entity.Property(e => e.ApprovalStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("approval_status");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.DesignImageId).HasColumnName("design_image_id");
            entity.Property(e => e.GenerationTimeSeconds)
                .HasPrecision(10, 2)
                .HasColumnName("generation_time_seconds");
            entity.Property(e => e.IsFinal)
                .HasDefaultValue(true)
                .HasColumnName("is_final");
            entity.Property(e => e.MockupHeightPx).HasColumnName("mockup_height_px");
            entity.Property(e => e.MockupImageUrl).HasColumnName("mockup_image_url");
            entity.Property(e => e.MockupTemplateId).HasColumnName("mockup_template_id");
            entity.Property(e => e.MockupWidthPx).HasColumnName("mockup_width_px");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.StorageKey)
                .HasMaxLength(500)
                .HasColumnName("storage_key");
            entity.Property(e => e.StorageProvider)
                .HasMaxLength(50)
                .HasDefaultValueSql("'s3'::character varying")
                .HasColumnName("storage_provider");

            entity.HasOne(d => d.ApiUsageRecord).WithMany(p => p.MockupImages)
                .HasForeignKey(d => d.ApiUsageRecordId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("mockup_images_api_usage_record_id_fkey");

            entity.HasOne(d => d.DesignImage).WithMany(p => p.MockupImages)
                .HasForeignKey(d => d.DesignImageId)
                .HasConstraintName("mockup_images_design_image_id_fkey");

            entity.HasOne(d => d.MockupTemplate).WithMany(p => p.MockupImages)
                .HasForeignKey(d => d.MockupTemplateId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("mockup_images_mockup_template_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.MockupImages)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("mockup_images_product_id_fkey");
        });

        modelBuilder.Entity<MockupTemplate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("mockup_templates_pkey");

            entity.ToTable("mockup_templates");

            entity.HasIndex(e => e.IsActive, "idx_mockup_templates_is_active");

            entity.HasIndex(e => e.ProductType, "idx_mockup_templates_product_type");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.BaseImageUrl).HasColumnName("base_image_url");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsSystemTemplate)
                .HasDefaultValue(true)
                .HasColumnName("is_system_template");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.OutputHeightPx).HasColumnName("output_height_px");
            entity.Property(e => e.OutputWidthPx).HasColumnName("output_width_px");
            entity.Property(e => e.PreviewImageUrl).HasColumnName("preview_image_url");
            entity.Property(e => e.PrintAreaConfig)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("print_area_config");
            entity.Property(e => e.ProductType)
                .HasMaxLength(50)
                .HasColumnName("product_type");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UsageCount)
                .HasDefaultValue(0)
                .HasColumnName("usage_count");
        });

        modelBuilder.Entity<MusicTrack>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("music_tracks_pkey");

            entity.ToTable("music_tracks");

            entity.HasIndex(e => e.Genre, "idx_music_tracks_genre");

            entity.HasIndex(e => e.Mood, "idx_music_tracks_mood");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ArtistName)
                .HasMaxLength(255)
                .HasColumnName("artist_name");
            entity.Property(e => e.AudioUrl).HasColumnName("audio_url");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.DurationSeconds).HasColumnName("duration_seconds");
            entity.Property(e => e.Genre)
                .HasMaxLength(100)
                .HasColumnName("genre");
            entity.Property(e => e.IsAvailable)
                .HasDefaultValue(true)
                .HasColumnName("is_available");
            entity.Property(e => e.LicenseSource)
                .HasMaxLength(255)
                .HasColumnName("license_source");
            entity.Property(e => e.LicenseType)
                .HasMaxLength(100)
                .HasColumnName("license_type");
            entity.Property(e => e.Mood)
                .HasMaxLength(100)
                .HasColumnName("mood");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.PreviewUrl).HasColumnName("preview_url");
            entity.Property(e => e.RoyaltyFree)
                .HasDefaultValue(false)
                .HasColumnName("royalty_free");
        });

        modelBuilder.Entity<NotificationAlert>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("notification_alerts_pkey");

            entity.ToTable("notification_alerts");

            entity.HasIndex(e => e.CreatedAt, "idx_notification_alerts_created_at");

            entity.HasIndex(e => e.IsRead, "idx_notification_alerts_is_read");

            entity.HasIndex(e => e.Severity, "idx_notification_alerts_severity");

            entity.HasIndex(e => e.UserId, "idx_notification_alerts_user_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ActionUrl)
                .HasMaxLength(500)
                .HasColumnName("action_url");
            entity.Property(e => e.BatchJobId).HasColumnName("batch_job_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.IsRead)
                .HasDefaultValue(false)
                .HasColumnName("is_read");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.ReadAt).HasColumnName("read_at");
            entity.Property(e => e.Severity)
                .HasMaxLength(50)
                .HasColumnName("severity");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.Type)
                .HasMaxLength(100)
                .HasColumnName("type");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.BatchJob).WithMany(p => p.NotificationAlerts)
                .HasForeignKey(d => d.BatchJobId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("notification_alerts_batch_job_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.NotificationAlerts)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("notification_alerts_user_id_fkey");
        });

        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("notification_deliveries_pkey");

            entity.ToTable("notification_deliveries");

            entity.HasIndex(e => e.DeliveryStatus, "idx_notification_deliveries_status");

            entity.HasIndex(e => new { e.NotificationAlertId, e.Channel }, "uq_notification_delivery_channel").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Channel)
                .HasMaxLength(50)
                .HasColumnName("channel");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeliveryStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("delivery_status");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.NotificationAlertId).HasColumnName("notification_alert_id");
            entity.Property(e => e.RetryCount)
                .HasDefaultValue(0)
                .HasColumnName("retry_count");
            entity.Property(e => e.SentAt).HasColumnName("sent_at");

            entity.HasOne(d => d.NotificationAlert).WithMany(p => p.NotificationDeliveries)
                .HasForeignKey(d => d.NotificationAlertId)
                .HasConstraintName("notification_deliveries_notification_alert_id_fkey");
        });

        modelBuilder.Entity<PaymentMethod>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("payment_methods_pkey");

            entity.ToTable("payment_methods");

            entity.HasIndex(e => e.UserId, "idx_payment_methods_user_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CardBrand)
                .HasMaxLength(50)
                .HasColumnName("card_brand");
            entity.Property(e => e.CardLast4Digits)
                .HasMaxLength(4)
                .HasColumnName("card_last_4_digits");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsDefault)
                .HasDefaultValue(false)
                .HasColumnName("is_default");
            entity.Property(e => e.PaymentType)
                .HasMaxLength(50)
                .HasColumnName("payment_type");
            entity.Property(e => e.PaypalEmailEncrypted).HasColumnName("paypal_email_encrypted");
            entity.Property(e => e.StripePaymentMethodId)
                .HasMaxLength(255)
                .HasColumnName("stripe_payment_method_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.PaymentMethods)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("payment_methods_user_id_fkey");
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("permissions_pkey");

            entity.ToTable("permissions");

            entity.HasIndex(e => new { e.Resource, e.Action }, "idx_permissions_resource_action");

            entity.HasIndex(e => e.Code, "permissions_code_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Action)
                .HasMaxLength(50)
                .HasColumnName("action");
            entity.Property(e => e.Code)
                .HasMaxLength(100)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Resource)
                .HasMaxLength(100)
                .HasColumnName("resource");
        });

        modelBuilder.Entity<PlanFeature>(entity =>
        {
            entity.HasKey(e => new { e.PlanId, e.FeatureCode }).HasName("plan_features_pkey");

            entity.ToTable("plan_features");

            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.FeatureCode)
                .HasMaxLength(100)
                .HasColumnName("feature_code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsEnabled)
                .HasDefaultValue(true)
                .HasColumnName("is_enabled");
            entity.Property(e => e.LimitValue).HasColumnName("limit_value");

            entity.HasOne(d => d.Plan).WithMany(p => p.PlanFeatures)
                .HasForeignKey(d => d.PlanId)
                .HasConstraintName("plan_features_plan_id_fkey");
        });

        modelBuilder.Entity<PrintifyIntegration>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("printify_integrations_pkey");

            entity.ToTable("printify_integrations");

            entity.HasIndex(e => e.PrintifyStoreId, "idx_printify_integrations_store_id");

            entity.HasIndex(e => e.UserId, "idx_printify_integrations_user_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsDefault)
                .HasDefaultValue(false)
                .HasColumnName("is_default");
            entity.Property(e => e.LastSyncAt).HasColumnName("last_sync_at");
            entity.Property(e => e.PrintifyApiTokenEncrypted).HasColumnName("printify_api_token_encrypted");
            entity.Property(e => e.PrintifyStoreId)
                .HasMaxLength(255)
                .HasColumnName("printify_store_id");
            entity.Property(e => e.ShopName)
                .HasMaxLength(255)
                .HasColumnName("shop_name");
            entity.Property(e => e.ShopTitle)
                .HasMaxLength(255)
                .HasColumnName("shop_title");
            entity.Property(e => e.TotalProductsUploaded)
                .HasDefaultValue(0)
                .HasColumnName("total_products_uploaded");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.PrintifyIntegrations)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("printify_integrations_user_id_fkey");
        });

        modelBuilder.Entity<PrintifyUploadLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("printify_upload_logs_pkey");

            entity.ToTable("printify_upload_logs");

            entity.HasIndex(e => e.PrintifyIntegrationId, "idx_printify_upload_logs_integration_id");

            entity.HasIndex(e => e.ProductId, "idx_printify_upload_logs_product_id");

            entity.HasIndex(e => e.UploadStatus, "idx_printify_upload_logs_status");

            entity.HasIndex(e => new { e.PrintifyIntegrationId, e.IdempotencyKey }, "uq_printify_idempotency").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ApiResponse)
                .HasColumnType("jsonb")
                .HasColumnName("api_response");
            entity.Property(e => e.AttemptedAt).HasColumnName("attempted_at");
            entity.Property(e => e.BatchJobId).HasColumnName("batch_job_id");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(255)
                .HasColumnName("idempotency_key");
            entity.Property(e => e.PrintifyIntegrationId).HasColumnName("printify_integration_id");
            entity.Property(e => e.PrintifyProductId)
                .HasMaxLength(255)
                .HasColumnName("printify_product_id");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.RetryCount)
                .HasDefaultValue(0)
                .HasColumnName("retry_count");
            entity.Property(e => e.SyncType)
                .HasMaxLength(50)
                .HasColumnName("sync_type");
            entity.Property(e => e.UploadPayload)
                .HasColumnType("jsonb")
                .HasColumnName("upload_payload");
            entity.Property(e => e.UploadStatus)
                .HasMaxLength(50)
                .HasColumnName("upload_status");

            entity.HasOne(d => d.BatchJob).WithMany(p => p.PrintifyUploadLogs)
                .HasForeignKey(d => d.BatchJobId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("printify_upload_logs_batch_job_id_fkey");

            entity.HasOne(d => d.PrintifyIntegration).WithMany(p => p.PrintifyUploadLogs)
                .HasForeignKey(d => d.PrintifyIntegrationId)
                .HasConstraintName("printify_upload_logs_printify_integration_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.PrintifyUploadLogs)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("printify_upload_logs_product_id_fkey");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("products_pkey");

            entity.ToTable("products");

            entity.HasIndex(e => e.CreatedAt, "idx_products_created_at");

            entity.HasIndex(e => e.DesignTemplateId, "idx_products_design_template_id");

            entity.HasIndex(e => e.ProcessingStatus, "idx_products_processing_status");

            entity.HasIndex(e => e.UserId, "idx_products_user_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ColorPreference)
                .HasMaxLength(255)
                .HasColumnName("color_preference");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.DesignTemplateId).HasColumnName("design_template_id");
            entity.Property(e => e.DesiredDesignText).HasColumnName("desired_design_text");
            entity.Property(e => e.InputDescription).HasColumnName("input_description");
            entity.Property(e => e.MainKeywords)
                .HasMaxLength(500)
                .HasColumnName("main_keywords");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.NicheCategory)
                .HasMaxLength(255)
                .HasColumnName("niche_category");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.ProcessingStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("processing_status");
            entity.Property(e => e.ProductType)
                .HasMaxLength(50)
                .HasColumnName("product_type");
            entity.Property(e => e.StylePreset)
                .HasMaxLength(100)
                .HasColumnName("style_preset");
            entity.Property(e => e.TargetAudience)
                .HasMaxLength(255)
                .HasColumnName("target_audience");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.DesignTemplate).WithMany(p => p.Products)
                .HasForeignKey(d => d.DesignTemplateId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("products_design_template_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Products)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("products_user_id_fkey");
        });

        modelBuilder.Entity<ProductMockupTemplate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("product_mockup_templates_pkey");

            entity.ToTable("product_mockup_templates");

            entity.HasIndex(e => e.MockupTemplateId, "idx_product_mockup_templates_template_id");

            entity.HasIndex(e => new { e.ProductId, e.MockupTemplateId }, "uq_product_mockup_template").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.MockupTemplateId).HasColumnName("mockup_template_id");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.SequenceOrder)
                .HasDefaultValue(1)
                .HasColumnName("sequence_order");

            entity.HasOne(d => d.MockupTemplate).WithMany(p => p.ProductMockupTemplates)
                .HasForeignKey(d => d.MockupTemplateId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("product_mockup_templates_mockup_template_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductMockupTemplates)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("product_mockup_templates_product_id_fkey");
        });

        modelBuilder.Entity<PromoVideo>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("promo_videos_pkey");

            entity.ToTable("promo_videos");

            entity.HasIndex(e => e.ApprovalStatus, "idx_promo_videos_approval_status");

            entity.HasIndex(e => e.CreatedAt, "idx_promo_videos_created_at");

            entity.HasIndex(e => e.ProductId, "idx_promo_videos_product_id");

            entity.HasIndex(e => e.Status, "idx_promo_videos_status");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ApiUsageRecordId).HasColumnName("api_usage_record_id");
            entity.Property(e => e.ApprovalStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("approval_status");
            entity.Property(e => e.AspectRatio)
                .HasMaxLength(20)
                .HasColumnName("aspect_ratio");
            entity.Property(e => e.BatchJobId).HasColumnName("batch_job_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.FileFormat)
                .HasMaxLength(10)
                .HasColumnName("file_format");
            entity.Property(e => e.FileSizeMb)
                .HasPrecision(10, 2)
                .HasColumnName("file_size_mb");
            entity.Property(e => e.GenerationTimeSeconds)
                .HasPrecision(10, 2)
                .HasColumnName("generation_time_seconds");
            entity.Property(e => e.IsFinal)
                .HasDefaultValue(false)
                .HasColumnName("is_final");
            entity.Property(e => e.MusicTrackId).HasColumnName("music_track_id");
            entity.Property(e => e.PlatformTarget)
                .HasMaxLength(50)
                .HasColumnName("platform_target");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.QualityScore)
                .HasPrecision(3, 2)
                .HasColumnName("quality_score");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.StorageKey)
                .HasMaxLength(500)
                .HasColumnName("storage_key");
            entity.Property(e => e.StorageProvider)
                .HasMaxLength(50)
                .HasDefaultValueSql("'s3'::character varying")
                .HasColumnName("storage_provider");
            entity.Property(e => e.TextOverlayColor)
                .HasMaxLength(7)
                .HasColumnName("text_overlay_color");
            entity.Property(e => e.TextOverlayContent).HasColumnName("text_overlay_content");
            entity.Property(e => e.TextOverlayFont)
                .HasMaxLength(100)
                .HasColumnName("text_overlay_font");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserRating).HasColumnName("user_rating");
            entity.Property(e => e.VideoDurationSeconds).HasColumnName("video_duration_seconds");
            entity.Property(e => e.VideoResolution)
                .HasMaxLength(50)
                .HasColumnName("video_resolution");
            entity.Property(e => e.VideoTemplateId).HasColumnName("video_template_id");
            entity.Property(e => e.VideoUrl).HasColumnName("video_url");

            entity.HasOne(d => d.ApiUsageRecord).WithMany(p => p.PromoVideos)
                .HasForeignKey(d => d.ApiUsageRecordId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("promo_videos_api_usage_record_id_fkey");

            entity.HasOne(d => d.BatchJob).WithMany(p => p.PromoVideos)
                .HasForeignKey(d => d.BatchJobId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("promo_videos_batch_job_id_fkey");

            entity.HasOne(d => d.MusicTrack).WithMany(p => p.PromoVideos)
                .HasForeignKey(d => d.MusicTrackId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("promo_videos_music_track_id_fkey");

            entity.HasOne(d => d.Product).WithMany(p => p.PromoVideos)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("promo_videos_product_id_fkey");

            entity.HasOne(d => d.VideoTemplate).WithMany(p => p.PromoVideos)
                .HasForeignKey(d => d.VideoTemplateId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("promo_videos_video_template_id_fkey");
        });

        modelBuilder.Entity<PromoVideoScene>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("promo_video_scenes_pkey");

            entity.ToTable("promo_video_scenes");

            entity.HasIndex(e => e.DesignImageId, "idx_promo_video_scenes_design_image_id");

            entity.HasIndex(e => e.MockupImageId, "idx_promo_video_scenes_mockup_image_id");

            entity.HasIndex(e => new { e.PromoVideoId, e.SceneOrder }, "uq_promo_video_scene_order").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DesignImageId).HasColumnName("design_image_id");
            entity.Property(e => e.DurationSeconds)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("3")
                .HasColumnName("duration_seconds");
            entity.Property(e => e.MockupImageId).HasColumnName("mockup_image_id");
            entity.Property(e => e.PromoVideoId).HasColumnName("promo_video_id");
            entity.Property(e => e.SceneOrder).HasColumnName("scene_order");
            entity.Property(e => e.TextOverlayContent).HasColumnName("text_overlay_content");
            entity.Property(e => e.TransitionEffect)
                .HasMaxLength(50)
                .HasColumnName("transition_effect");

            entity.HasOne(d => d.DesignImage).WithMany(p => p.PromoVideoScenes)
                .HasForeignKey(d => d.DesignImageId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("promo_video_scenes_design_image_id_fkey");

            entity.HasOne(d => d.MockupImage).WithMany(p => p.PromoVideoScenes)
                .HasForeignKey(d => d.MockupImageId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("promo_video_scenes_mockup_image_id_fkey");

            entity.HasOne(d => d.PromoVideo).WithMany(p => p.PromoVideoScenes)
                .HasForeignKey(d => d.PromoVideoId)
                .HasConstraintName("promo_video_scenes_promo_video_id_fkey");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("roles_pkey");

            entity.ToTable("roles");

            entity.HasIndex(e => e.Code, "idx_roles_code");

            entity.HasIndex(e => e.Code, "roles_code_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsSystemRole)
                .HasDefaultValue(true)
                .HasColumnName("is_system_role");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(e => new { e.RoleId, e.PermissionId }).HasName("role_permissions_pkey");

            entity.ToTable("role_permissions");

            entity.HasIndex(e => e.PermissionId, "idx_role_permissions_permission_id");

            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.PermissionId).HasColumnName("permission_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Permission).WithMany(p => p.RolePermissions)
                .HasForeignKey(d => d.PermissionId)
                .HasConstraintName("role_permissions_permission_id_fkey");

            entity.HasOne(d => d.Role).WithMany(p => p.RolePermissions)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("role_permissions_role_id_fkey");
        });

        modelBuilder.Entity<SeoScore>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("seo_scores_pkey");

            entity.ToTable("seo_scores");

            entity.HasIndex(e => e.OverallSeoScore, "idx_seo_scores_overall");

            entity.HasIndex(e => e.ListingContentId, "seo_scores_listing_content_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CalculatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("calculated_at");
            entity.Property(e => e.DescriptionScore)
                .HasPrecision(5, 2)
                .HasColumnName("description_score");
            entity.Property(e => e.ImprovementSuggestions)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("improvement_suggestions");
            entity.Property(e => e.KeywordDensity)
                .HasPrecision(5, 2)
                .HasColumnName("keyword_density");
            entity.Property(e => e.KeywordOptimizationScore)
                .HasPrecision(5, 2)
                .HasColumnName("keyword_optimization_score");
            entity.Property(e => e.ListingContentId).HasColumnName("listing_content_id");
            entity.Property(e => e.OverallSeoScore)
                .HasPrecision(5, 2)
                .HasColumnName("overall_seo_score");
            entity.Property(e => e.ScoringAlgorithmVersion)
                .HasMaxLength(20)
                .HasDefaultValueSql("'v1'::character varying")
                .HasColumnName("scoring_algorithm_version");
            entity.Property(e => e.TagRelevanceScore)
                .HasPrecision(5, 2)
                .HasColumnName("tag_relevance_score");
            entity.Property(e => e.TagsScore)
                .HasPrecision(5, 2)
                .HasColumnName("tags_score");
            entity.Property(e => e.TitleScore)
                .HasPrecision(5, 2)
                .HasColumnName("title_score");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.ListingContent).WithOne(p => p.SeoScore)
                .HasForeignKey<SeoScore>(d => d.ListingContentId)
                .HasConstraintName("seo_scores_listing_content_id_fkey");
        });

        modelBuilder.Entity<ShareHashtag>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("share_hashtags_pkey");

            entity.ToTable("share_hashtags");

            entity.HasIndex(e => new { e.SocialMediaShareId, e.Position }, "uq_share_hashtag_position").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Hashtag)
                .HasMaxLength(100)
                .HasColumnName("hashtag");
            entity.Property(e => e.Position).HasColumnName("position");
            entity.Property(e => e.SocialMediaShareId).HasColumnName("social_media_share_id");

            entity.HasOne(d => d.SocialMediaShare).WithMany(p => p.ShareHashtags)
                .HasForeignKey(d => d.SocialMediaShareId)
                .HasConstraintName("share_hashtags_social_media_share_id_fkey");
        });

        modelBuilder.Entity<SocialMediaShare>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("social_media_shares_pkey");

            entity.ToTable("social_media_shares");

            entity.HasIndex(e => e.Platform, "idx_social_media_shares_platform");

            entity.HasIndex(e => e.ProductId, "idx_social_media_shares_product_id");

            entity.HasIndex(e => e.ShareStatus, "idx_social_media_shares_status");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ApiResponse)
                .HasColumnType("jsonb")
                .HasColumnName("api_response");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.ExternalPlatformUrl).HasColumnName("external_platform_url");
            entity.Property(e => e.ExternalPostId)
                .HasMaxLength(255)
                .HasColumnName("external_post_id");
            entity.Property(e => e.Platform)
                .HasMaxLength(50)
                .HasColumnName("platform");
            entity.Property(e => e.PostCaption).HasColumnName("post_caption");
            entity.Property(e => e.PostedTime).HasColumnName("posted_time");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.PromoVideoId).HasColumnName("promo_video_id");
            entity.Property(e => e.RetryCount)
                .HasDefaultValue(0)
                .HasColumnName("retry_count");
            entity.Property(e => e.ScheduledTime).HasColumnName("scheduled_time");
            entity.Property(e => e.ShareStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'pending'::character varying")
                .HasColumnName("share_status");

            entity.HasOne(d => d.Product).WithMany(p => p.SocialMediaShares)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("social_media_shares_product_id_fkey");

            entity.HasOne(d => d.PromoVideo).WithMany(p => p.SocialMediaShares)
                .HasForeignKey(d => d.PromoVideoId)
                .HasConstraintName("social_media_shares_promo_video_id_fkey");
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("subscriptions_pkey");

            entity.ToTable("subscriptions");

            entity.HasIndex(e => e.CreatedAt, "idx_subscriptions_created_at");

            entity.HasIndex(e => e.RenewalDate, "idx_subscriptions_renewal_date");

            entity.HasIndex(e => e.Status, "idx_subscriptions_status");

            entity.HasIndex(e => e.UserId, "idx_subscriptions_user_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AnnualPriceUsd)
                .HasPrecision(10, 2)
                .HasColumnName("annual_price_usd");
            entity.Property(e => e.AutoRenew)
                .HasDefaultValue(true)
                .HasColumnName("auto_renew");
            entity.Property(e => e.BillingCycle)
                .HasMaxLength(50)
                .HasColumnName("billing_cycle");
            entity.Property(e => e.CancelledAt).HasColumnName("cancelled_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.MonthlyPriceUsd)
                .HasPrecision(10, 2)
                .HasColumnName("monthly_price_usd");
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.RenewalDate).HasColumnName("renewal_date");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'active'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.TrialEndsAt).HasColumnName("trial_ends_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Plan).WithMany(p => p.Subscriptions)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("subscriptions_plan_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Subscriptions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("subscriptions_user_id_fkey");
        });

        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("subscription_plans_pkey");

            entity.ToTable("subscription_plans");

            entity.HasIndex(e => e.IsActive, "idx_subscription_plans_is_active");

            entity.HasIndex(e => e.Tier, "idx_subscription_plans_tier");

            entity.HasIndex(e => e.Tier, "subscription_plans_tier_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AnnualPriceUsd)
                .HasPrecision(10, 2)
                .HasColumnName("annual_price_usd");
            entity.Property(e => e.ApiCallQuota)
                .HasDefaultValue(10000)
                .HasColumnName("api_call_quota");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomApiKeysAllowed)
                .HasDefaultValue(false)
                .HasColumnName("custom_api_keys_allowed");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.ImageGenerationQuota)
                .HasDefaultValue(500)
                .HasColumnName("image_generation_quota");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.MaxBatchSize)
                .HasDefaultValue(100)
                .HasColumnName("max_batch_size");
            entity.Property(e => e.MaxConcurrentJobs)
                .HasDefaultValue(1)
                .HasColumnName("max_concurrent_jobs");
            entity.Property(e => e.MaxProductsPerMonth)
                .HasDefaultValue(1000)
                .HasColumnName("max_products_per_month");
            entity.Property(e => e.MonthlyPriceUsd)
                .HasPrecision(10, 2)
                .HasColumnName("monthly_price_usd");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.PrioritySupport)
                .HasDefaultValue(false)
                .HasColumnName("priority_support");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0)
                .HasColumnName("sort_order");
            entity.Property(e => e.StorageQuotaGb)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("100")
                .HasColumnName("storage_quota_gb");
            entity.Property(e => e.Tier)
                .HasMaxLength(50)
                .HasColumnName("tier");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.VideoGenerationQuota)
                .HasDefaultValue(50)
                .HasColumnName("video_generation_quota");
        });

        modelBuilder.Entity<SupportTicket>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("support_tickets_pkey");

            entity.ToTable("support_tickets");

            entity.HasIndex(e => e.AssignedTo, "idx_support_tickets_assigned_to");

            entity.HasIndex(e => e.CreatedAt, "idx_support_tickets_created_at");

            entity.HasIndex(e => e.Priority, "idx_support_tickets_priority");

            entity.HasIndex(e => e.Status, "idx_support_tickets_status");

            entity.HasIndex(e => e.UserId, "idx_support_tickets_user_id");

            entity.HasIndex(e => e.TicketNumber, "support_tickets_ticket_number_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AssignedTo).HasColumnName("assigned_to");
            entity.Property(e => e.Category)
                .HasMaxLength(100)
                .HasColumnName("category");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Priority)
                .HasMaxLength(50)
                .HasDefaultValueSql("'normal'::character varying")
                .HasColumnName("priority");
            entity.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
            entity.Property(e => e.SatisfactionRating).HasColumnName("satisfaction_rating");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'open'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.Subject)
                .HasMaxLength(255)
                .HasColumnName("subject");
            entity.Property(e => e.TicketNumber)
                .HasMaxLength(50)
                .HasColumnName("ticket_number");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.AssignedToNavigation).WithMany(p => p.SupportTicketAssignedToNavigations)
                .HasForeignKey(d => d.AssignedTo)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("support_tickets_assigned_to_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.SupportTicketUsers)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("support_tickets_user_id_fkey");
        });

        modelBuilder.Entity<TicketAttachment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ticket_attachments_pkey");

            entity.ToTable("ticket_attachments");

            entity.HasIndex(e => e.TicketReplyId, "idx_ticket_attachments_reply_id");

            entity.HasIndex(e => e.SupportTicketId, "idx_ticket_attachments_ticket_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .HasColumnName("file_name");
            entity.Property(e => e.FileSizeMb)
                .HasPrecision(10, 2)
                .HasColumnName("file_size_mb");
            entity.Property(e => e.FileUrl).HasColumnName("file_url");
            entity.Property(e => e.MimeType)
                .HasMaxLength(100)
                .HasColumnName("mime_type");
            entity.Property(e => e.SupportTicketId).HasColumnName("support_ticket_id");
            entity.Property(e => e.TicketReplyId).HasColumnName("ticket_reply_id");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");

            entity.HasOne(d => d.SupportTicket).WithMany(p => p.TicketAttachments)
                .HasForeignKey(d => d.SupportTicketId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ticket_attachments_support_ticket_id_fkey");

            entity.HasOne(d => d.TicketReply).WithMany(p => p.TicketAttachments)
                .HasForeignKey(d => d.TicketReplyId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ticket_attachments_ticket_reply_id_fkey");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.TicketAttachments)
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("ticket_attachments_uploaded_by_fkey");
        });

        modelBuilder.Entity<TicketReply>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ticket_replies_pkey");

            entity.ToTable("ticket_replies");

            entity.HasIndex(e => e.AuthorId, "idx_ticket_replies_author_id");

            entity.HasIndex(e => e.SupportTicketId, "idx_ticket_replies_support_ticket_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AuthorId).HasColumnName("author_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsInternalNote)
                .HasDefaultValue(false)
                .HasColumnName("is_internal_note");
            entity.Property(e => e.ReplyText).HasColumnName("reply_text");
            entity.Property(e => e.SupportTicketId).HasColumnName("support_ticket_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Author).WithMany(p => p.TicketReplies)
                .HasForeignKey(d => d.AuthorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("ticket_replies_author_id_fkey");

            entity.HasOne(d => d.SupportTicket).WithMany(p => p.TicketReplies)
                .HasForeignKey(d => d.SupportTicketId)
                .HasConstraintName("ticket_replies_support_ticket_id_fkey");
        });

        modelBuilder.Entity<UsageStatistic>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("usage_statistics_pkey");

            entity.ToTable("usage_statistics");

            entity.HasIndex(e => e.UserId, "idx_usage_statistics_user_id");

            entity.HasIndex(e => new { e.UserId, e.BillingPeriodStart, e.BillingPeriodEnd }, "uq_usage_statistics_period").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AverageProcessingTimeSeconds)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("average_processing_time_seconds");
            entity.Property(e => e.BatchJobsCompleted)
                .HasDefaultValue(0)
                .HasColumnName("batch_jobs_completed");
            entity.Property(e => e.BillingPeriodEnd).HasColumnName("billing_period_end");
            entity.Property(e => e.BillingPeriodStart).HasColumnName("billing_period_start");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ImagesGenerated)
                .HasDefaultValue(0)
                .HasColumnName("images_generated");
            entity.Property(e => e.ListingsExported)
                .HasDefaultValue(0)
                .HasColumnName("listings_exported");
            entity.Property(e => e.StorageUsedGb)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("storage_used_gb");
            entity.Property(e => e.TotalApiCalls)
                .HasDefaultValue(0)
                .HasColumnName("total_api_calls");
            entity.Property(e => e.TotalApiCostUsd)
                .HasPrecision(12, 6)
                .HasDefaultValueSql("0")
                .HasColumnName("total_api_cost_usd");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.VideosCreated)
                .HasDefaultValue(0)
                .HasColumnName("videos_created");

            entity.HasOne(d => d.User).WithMany(p => p.UsageStatistics)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("usage_statistics_user_id_fkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.AccountStatus, "idx_users_account_status");

            entity.HasIndex(e => e.DeletedAt, "idx_users_deleted_at");

            entity.HasIndex(e => e.Email, "idx_users_email");

            entity.HasIndex(e => e.OauthGoogleId, "idx_users_oauth_google_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AccountStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'active'::character varying")
                .HasColumnName("account_status");
            entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.EmailVerified)
                .HasDefaultValue(false)
                .HasColumnName("email_verified");
            entity.Property(e => e.EmailVerifiedAt).HasColumnName("email_verified_at");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(e => e.OauthGoogleId)
                .HasMaxLength(255)
                .HasColumnName("oauth_google_id");
            entity.Property(e => e.OauthProvider)
                .HasMaxLength(50)
                .HasColumnName("oauth_provider");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_profiles_pkey");

            entity.ToTable("user_profiles");

            entity.HasIndex(e => e.UserId, "idx_user_profiles_user_id");

            entity.HasIndex(e => e.UserId, "user_profiles_user_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Language)
                .HasMaxLength(10)
                .HasDefaultValueSql("'en'::character varying")
                .HasColumnName("language");
            entity.Property(e => e.NewsletterSubscribed)
                .HasDefaultValue(false)
                .HasColumnName("newsletter_subscribed");
            entity.Property(e => e.NotificationEmailEnabled)
                .HasDefaultValue(true)
                .HasColumnName("notification_email_enabled");
            entity.Property(e => e.ProfileCompletionPercentage)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("profile_completion_percentage");
            entity.Property(e => e.ShopDescription).HasColumnName("shop_description");
            entity.Property(e => e.ShopName)
                .HasMaxLength(255)
                .HasColumnName("shop_name");
            entity.Property(e => e.ThemePreference)
                .HasMaxLength(50)
                .HasDefaultValueSql("'light'::character varying")
                .HasColumnName("theme_preference");
            entity.Property(e => e.Timezone)
                .HasMaxLength(50)
                .HasDefaultValueSql("'UTC'::character varying")
                .HasColumnName("timezone");
            entity.Property(e => e.TwoFactorEnabled)
                .HasDefaultValue(false)
                .HasColumnName("two_factor_enabled");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithOne(p => p.UserProfile)
                .HasForeignKey<UserProfile>(d => d.UserId)
                .HasConstraintName("user_profiles_user_id_fkey");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RoleId }).HasName("user_roles_pkey");

            entity.ToTable("user_roles");

            entity.HasIndex(e => e.RoleId, "idx_user_roles_role_id");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.GrantedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("granted_at");
            entity.Property(e => e.GrantedBy).HasColumnName("granted_by");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");

            entity.HasOne(d => d.GrantedByNavigation).WithMany(p => p.UserRoleGrantedByNavigations)
                .HasForeignKey(d => d.GrantedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("user_roles_granted_by_fkey");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("user_roles_role_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoleUsers)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("user_roles_user_id_fkey");
        });

        modelBuilder.Entity<VideoTemplate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("video_templates_pkey");

            entity.ToTable("video_templates");

            entity.HasIndex(e => e.Platform, "idx_video_templates_platform");

            entity.HasIndex(e => e.Type, "idx_video_templates_type");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AspectRatio)
                .HasMaxLength(20)
                .HasColumnName("aspect_ratio");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DurationSeconds).HasColumnName("duration_seconds");
            entity.Property(e => e.EffectsConfig)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("effects_config");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsSystemTemplate)
                .HasDefaultValue(true)
                .HasColumnName("is_system_template");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.Platform)
                .HasMaxLength(50)
                .HasColumnName("platform");
            entity.Property(e => e.PreviewVideoUrl).HasColumnName("preview_video_url");
            entity.Property(e => e.Resolution)
                .HasMaxLength(50)
                .HasColumnName("resolution");
            entity.Property(e => e.Type)
                .HasMaxLength(100)
                .HasColumnName("type");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
