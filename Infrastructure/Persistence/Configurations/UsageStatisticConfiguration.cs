using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures usage statistic persistence.
/// </summary>
public sealed class UsageStatisticConfiguration : IEntityTypeConfiguration<UsageStatistic>
{
    public void Configure(EntityTypeBuilder<UsageStatistic> builder)
    {
        builder.ToTable("usage_statistics", table =>
        {
            table.HasCheckConstraint("ck_usage_statistics_period", "billing_period_end >= billing_period_start");
            table.HasCheckConstraint("ck_usage_statistics_counters", "images_generated >= 0 AND videos_created >= 0 AND listings_exported >= 0 AND total_api_calls >= 0 AND batch_jobs_completed >= 0");
            table.HasCheckConstraint("ck_usage_statistics_costs", "total_api_cost_usd >= 0 AND storage_used_gb >= 0 AND average_processing_time_seconds >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(statistic => statistic.ImagesGenerated).HasDefaultValue(0);
        builder.Property(statistic => statistic.VideosCreated).HasDefaultValue(0);
        builder.Property(statistic => statistic.ListingsExported).HasDefaultValue(0);
        builder.Property(statistic => statistic.TotalApiCalls).HasDefaultValue(0);
        builder.Property(statistic => statistic.TotalApiCostUsd).HasPrecision(10, 4).HasDefaultValue(0m);
        builder.Property(statistic => statistic.StorageUsedGb).HasPrecision(10, 2).HasDefaultValue(0m);
        builder.Property(statistic => statistic.BatchJobsCompleted).HasDefaultValue(0);
        builder.Property(statistic => statistic.AverageProcessingTimeSeconds).HasPrecision(10, 2).HasDefaultValue(0m);

        builder.HasIndex(statistic => statistic.SellerId);
        builder.HasIndex(statistic => new { statistic.BillingPeriodStart, statistic.BillingPeriodEnd });
        builder.HasIndex(statistic => new { statistic.SellerId, statistic.BillingPeriodStart, statistic.BillingPeriodEnd })
            .IsUnique();

        builder.HasOne<Seller>()
            .WithMany(seller => seller.UsageStatistics)
            .HasForeignKey(statistic => statistic.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
