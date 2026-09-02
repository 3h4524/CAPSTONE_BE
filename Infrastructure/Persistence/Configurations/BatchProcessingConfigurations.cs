using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

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
            table.HasCheckConstraint("ck_batch_jobs_progress", "progress_percentage BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_batch_jobs_counts", "total_products >= 0 AND processed_products >= 0 AND failed_products >= 0 AND skipped_products >= 0");
            table.HasCheckConstraint("ck_batch_jobs_costs", "estimated_cost_usd >= 0 AND actual_cost_usd >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(job => job.Name).HasMaxLength(150).IsRequired();
        builder.Property(job => job.Description).HasColumnType("text");
        builder.Property(job => job.SourceFileType).HasMaxLength(16).IsRequired();
        builder.Property(job => job.SourceFileUrl).HasColumnType("text");
        builder.Property(job => job.SourceFileHash).HasMaxLength(64);
        builder.Property(job => job.Status).HasMaxLength(24).HasDefaultValue("draft").IsRequired();
        builder.Property(job => job.ProgressPercentage).HasPrecision(5, 2).HasDefaultValue(0m);
        builder.Property(job => job.TotalProducts).HasDefaultValue(0);
        builder.Property(job => job.ProcessedProducts).HasDefaultValue(0);
        builder.Property(job => job.FailedProducts).HasDefaultValue(0);
        builder.Property(job => job.SkippedProducts).HasDefaultValue(0);
        builder.Property(job => job.Config).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(job => job.Priority).HasMaxLength(16).HasDefaultValue("normal").IsRequired();
        builder.Property(job => job.StartedAtUtc).HasColumnName("started_at");
        builder.Property(job => job.CompletedAtUtc).HasColumnName("completed_at");
        builder.Property(job => job.EstimatedCompletionTimeUtc).HasColumnName("estimated_completion_time");
        builder.Property(job => job.EstimatedCostUsd).HasPrecision(10, 2).HasDefaultValue(0m);
        builder.Property(job => job.ActualCostUsd).HasPrecision(10, 2).HasDefaultValue(0m);

        builder.HasIndex(job => job.SellerId);
        builder.HasIndex(job => job.Status);
        builder.HasIndex(job => new { job.SellerId, job.Status });
        builder.HasIndex(job => job.CreatedAtUtc).IsDescending();
        builder.HasIndex(job => job.StartedAtUtc);

        builder.HasOne<Seller>()
            .WithMany()
            .HasForeignKey(job => job.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures batch job product persistence.
/// </summary>
public sealed class BatchJobProductConfiguration : IEntityTypeConfiguration<BatchJobProduct>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BatchJobProduct> builder)
    {
        builder.ToTable("batch_job_products", table =>
        {
            table.HasCheckConstraint("ck_batch_job_products_sequence", "sequence_order >= 0");
            table.HasCheckConstraint("ck_batch_job_products_retry_count", "retry_count >= 0");
            table.HasCheckConstraint("ck_batch_job_products_duration", "duration_seconds IS NULL OR duration_seconds >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.HasQueryFilter(item => item.BatchJob.DeletedAtUtc == null);

        builder.Property(item => item.Status).HasMaxLength(24).HasDefaultValue("pending").IsRequired();
        builder.Property(item => item.StartedAtUtc).HasColumnName("started_at");
        builder.Property(item => item.CompletedAtUtc).HasColumnName("completed_at");
        builder.Property(item => item.DurationSeconds).HasPrecision(10, 2);
        builder.Property(item => item.ErrorMessage).HasColumnType("text");
        builder.Property(item => item.RetryCount).HasDefaultValue(0);

        builder.HasIndex(item => item.BatchJobId);
        builder.HasIndex(item => item.Status);
        builder.HasIndex(item => item.ProductId);
        builder.HasIndex(item => new { item.BatchJobId, item.Status });

        builder.HasOne(item => item.BatchJob)
            .WithMany(job => job.JobProducts)
            .HasForeignKey(item => item.BatchJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.Product)
            .WithMany(product => product.BatchItems)
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures batch job log persistence.
/// </summary>
public sealed class BatchJobLogConfiguration : IEntityTypeConfiguration<BatchJobLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BatchJobLog> builder)
    {
        builder.ToTable("batch_job_logs", table =>
        {
            table.HasCheckConstraint("ck_batch_job_logs_duration", "duration_ms IS NULL OR duration_ms >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.HasQueryFilter(log => log.BatchJob.DeletedAtUtc == null);

        builder.Property(log => log.LogLevel).HasMaxLength(16).IsRequired();
        builder.Property(log => log.EventType).HasMaxLength(64).IsRequired();
        builder.Property(log => log.Message).HasColumnType("text").IsRequired();
        builder.Property(log => log.Details).HasColumnType("jsonb");
        builder.Property(log => log.ApiCallIdentifier).HasMaxLength(128);

        builder.HasIndex(log => log.BatchJobId);
        builder.HasIndex(log => log.LogLevel);
        builder.HasIndex(log => log.CreatedAtUtc).IsDescending();

        builder.HasOne(log => log.BatchJob)
            .WithMany(job => job.Logs)
            .HasForeignKey(log => log.BatchJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(log => log.BatchJobProduct)
            .WithMany(item => item.Logs)
            .HasForeignKey(log => log.BatchJobProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
