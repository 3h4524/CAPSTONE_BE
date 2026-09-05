using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// The product pipeline states shared by batch rows and products.
/// </summary>
internal static class PipelineStatuses
{
    public const string Sql =
        "('pending', 'validating', 'queued', 'generating_image', 'image_review_required', " +
        "'generating_mockup', 'generating_video', 'generating_listing', 'review_required', " +
        "'approved', 'exported', 'published', 'failed', 'skipped', 'cancelled')";
}

/// <summary>
/// Configures batch job persistence.
/// </summary>
public sealed class BatchJobConfiguration : IEntityTypeConfiguration<BatchJob>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BatchJob> builder)
    {
        builder.ToTable("batch_jobs", table =>
        {
            table.HasCheckConstraint(
                "chk_batch_jobs_status",
                "status IN ('draft', 'validating', 'ready', 'queued', 'running', 'paused', 'cancelling', 'cancelled', 'partially_completed', 'completed', 'failed')");
            table.HasCheckConstraint("chk_batch_jobs_priority", "priority IN ('low', 'normal', 'high')");
            table.HasCheckConstraint("chk_batch_jobs_source_type", "source_file_type IN ('csv', 'xlsx', 'manual')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(job => job.Name).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(job => job.Description).HasColumnType("text");
        builder.Property(job => job.SourceFileType).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(job => job.SourceFileUrl).HasColumnType("text");
        builder.Property(job => job.SourceFileHash).HasMaxLength(ColumnLengths.Stamp);
        builder.Property(job => job.Status).HasMaxLength(ColumnLengths.Code).HasDefaultValue("draft").IsRequired();
        builder.Property(job => job.ProgressPercentage).HasPrecision(5, 2).HasDefaultValue(0m);
        builder.Property(job => job.Config).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(job => job.Priority).HasMaxLength(ColumnLengths.Code).HasDefaultValue("normal").IsRequired();
        builder.Property(job => job.EstimatedCostUsd).HasPrecision(10, 2).HasDefaultValue(0m);
        builder.Property(job => job.ActualCostUsd).HasPrecision(12, 6).HasDefaultValue(0m);
        builder.Property(job => job.TotalProducts).HasDefaultValue(0);
        builder.Property(job => job.ProcessedProducts).HasDefaultValue(0);
        builder.Property(job => job.FailedProducts).HasDefaultValue(0);
        builder.Property(job => job.SkippedProducts).HasDefaultValue(0);
        builder.Property(job => job.StartedAtUtc).HasColumnName("started_at");
        builder.Property(job => job.CompletedAtUtc).HasColumnName("completed_at");
        builder.Property(job => job.EstimatedCompletionTimeUtc).HasColumnName("estimated_completion_time");

        builder.HasIndex(job => job.UserId);
        builder.HasIndex(job => job.Status);
        builder.HasIndex(job => new { job.UserId, job.Status });
        builder.HasIndex(job => job.CreatedAtUtc);
        builder.HasIndex(job => job.StartedAtUtc);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(job => job.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures the product slots inside a batch job.
/// </summary>
public sealed class BatchJobProductConfiguration : IEntityTypeConfiguration<BatchJobProduct>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BatchJobProduct> builder)
    {
        builder.ToTable("batch_job_products", table =>
        {
            table.HasCheckConstraint("chk_batch_job_products_status", $"status IN {PipelineStatuses.Sql}");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(row => row.Status).HasMaxLength(ColumnLengths.Code).HasDefaultValue("pending").IsRequired();
        builder.Property(row => row.CurrentStep).HasMaxLength(ColumnLengths.Code);
        builder.Property(row => row.RawRowData).HasColumnType("jsonb");
        builder.Property(row => row.DurationSeconds).HasPrecision(10, 2);
        builder.Property(row => row.ErrorMessage).HasColumnType("text");
        builder.Property(row => row.RetryCount).HasDefaultValue(0);
        builder.Property(row => row.StartedAtUtc).HasColumnName("started_at");
        builder.Property(row => row.CompletedAtUtc).HasColumnName("completed_at");

        builder.HasIndex(row => new { row.BatchJobId, row.SequenceOrder })
            .HasDatabaseName("uq_batch_job_product_order")
            .IsUnique();

        builder.HasIndex(row => row.BatchJobId);
        builder.HasIndex(row => row.Status);
        builder.HasIndex(row => row.ProductId);
        builder.HasIndex(row => new { row.BatchJobId, row.Status });

        // Follows the parent's soft delete: a deleted job must not leave its rows visible.
        builder.HasQueryFilter(row => row.BatchJob.DeletedAtUtc == null);

        builder.HasOne(row => row.BatchJob)
            .WithMany(job => job.Products)
            .HasForeignKey(row => row.BatchJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(row => row.Product)
            .WithMany()
            .HasForeignKey(row => row.ProductId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures batch job log entries.
/// </summary>
public sealed class BatchJobLogConfiguration : IEntityTypeConfiguration<BatchJobLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BatchJobLog> builder)
    {
        builder.ToTable("batch_job_logs", table =>
        {
            table.HasCheckConstraint(
                "chk_batch_job_logs_level",
                "log_level IN ('debug', 'info', 'warning', 'error', 'critical')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(log => log.LogLevel).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(log => log.EventType).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(log => log.Message).HasColumnType("text").IsRequired();
        builder.Property(log => log.Details).HasColumnType("jsonb");

        builder.HasIndex(log => log.BatchJobId);
        builder.HasIndex(log => log.LogLevel);
        builder.HasIndex(log => log.CreatedAtUtc);

        builder.HasQueryFilter(log => log.BatchJob.DeletedAtUtc == null);

        builder.HasOne(log => log.BatchJob)
            .WithMany(job => job.Logs)
            .HasForeignKey(log => log.BatchJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(log => log.BatchJobProduct)
            .WithMany()
            .HasForeignKey(log => log.BatchJobProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(log => log.ApiUsageRecord)
            .WithMany()
            .HasForeignKey(log => log.ApiUsageRecordId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures export package persistence.
/// </summary>
public sealed class ExportPackageConfiguration : IEntityTypeConfiguration<ExportPackage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ExportPackage> builder)
    {
        builder.ToTable("export_packages", table =>
        {
            table.HasCheckConstraint("chk_export_packages_type", "type IN ('zip_package', 'listing_csv')");
            table.HasCheckConstraint("chk_export_packages_status", "status IN ('preparing', 'ready', 'failed', 'expired')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(package => package.Type).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(package => package.Name).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(package => package.Description).HasColumnType("text");
        builder.Property(package => package.StorageProvider).HasMaxLength(ColumnLengths.Code).HasDefaultValue("s3");
        builder.Property(package => package.StorageKey).HasMaxLength(ColumnLengths.StorageKey);
        builder.Property(package => package.DownloadUrl).HasColumnType("text");
        builder.Property(package => package.FileSizeMb).HasPrecision(10, 2);
        builder.Property(package => package.CreationTimeSeconds).HasPrecision(10, 2);
        builder.Property(package => package.Status).HasMaxLength(ColumnLengths.Code).HasDefaultValue("preparing").IsRequired();
        builder.Property(package => package.DownloadedAtUtc).HasColumnName("downloaded_at");
        builder.Property(package => package.ExpiresAtUtc).HasColumnName("expires_at").IsRequired();

        builder.HasIndex(package => package.BatchJobId);
        builder.HasIndex(package => package.UserId);
        builder.HasIndex(package => package.Status);
        builder.HasIndex(package => package.ExpiresAtUtc);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(package => package.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(package => package.BatchJob)
            .WithMany()
            .HasForeignKey(package => package.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures the products included in an export package.
/// </summary>
public sealed class ExportPackageItemConfiguration : IEntityTypeConfiguration<ExportPackageItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ExportPackageItem> builder)
    {
        builder.ToTable("export_package_items", table =>
        {
            table.HasCheckConstraint("chk_export_item_status", "item_status IN ('pending', 'packed', 'failed', 'skipped')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(item => item.IncludeDesignImages).HasDefaultValue(true);
        builder.Property(item => item.IncludeMockupImages).HasDefaultValue(true);
        builder.Property(item => item.IncludePromoVideo).HasDefaultValue(true);
        builder.Property(item => item.IncludeListingContent).HasDefaultValue(true);
        builder.Property(item => item.FolderPathInZip).HasMaxLength(ColumnLengths.StorageKey);
        builder.Property(item => item.ItemStatus).HasMaxLength(ColumnLengths.Code).HasDefaultValue("pending").IsRequired();
        builder.Property(item => item.ErrorMessage).HasColumnType("text");

        builder.HasIndex(item => new { item.ExportPackageId, item.ProductId })
            .HasDatabaseName("uq_export_package_product")
            .IsUnique();

        builder.HasIndex(item => item.ProductId);
        builder.HasIndex(item => item.ItemStatus);

        builder.HasQueryFilter(item => item.Product.DeletedAtUtc == null);

        builder.HasOne(item => item.ExportPackage)
            .WithMany(package => package.Items)
            .HasForeignKey(item => item.ExportPackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.Product)
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
