using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures subscription plan persistence.
/// </summary>
public sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("subscription_plans", table =>
        {
            table.HasCheckConstraint("ck_subscription_plans_monthly_price", "monthly_price_usd >= 0");
            table.HasCheckConstraint("ck_subscription_plans_annual_price", "annual_price_usd IS NULL OR annual_price_usd >= 0");
            table.HasCheckConstraint("ck_subscription_plans_quotas", "max_batch_size >= 0 AND max_products_per_month >= 0 AND max_concurrent_jobs >= 0 AND image_generation_quota >= 0 AND video_generation_quota >= 0 AND api_call_quota >= 0 AND storage_quota_gb >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(plan => plan.Name).HasMaxLength(80).IsRequired();
        builder.Property(plan => plan.Tier).HasMaxLength(32).IsRequired();
        builder.Property(plan => plan.Description).HasColumnType("text");
        builder.Property(plan => plan.MonthlyPriceUsd).HasPrecision(10, 2).HasDefaultValue(0m);
        builder.Property(plan => plan.AnnualPriceUsd).HasPrecision(10, 2);
        builder.Property(plan => plan.MaxBatchSize).HasDefaultValue(100);
        builder.Property(plan => plan.MaxProductsPerMonth).HasDefaultValue(1_000);
        builder.Property(plan => plan.MaxConcurrentJobs).HasDefaultValue(1);
        builder.Property(plan => plan.ImageGenerationQuota).HasDefaultValue(500);
        builder.Property(plan => plan.VideoGenerationQuota).HasDefaultValue(50);
        builder.Property(plan => plan.ApiCallQuota).HasDefaultValue(10_000);
        builder.Property(plan => plan.StorageQuotaGb).HasPrecision(10, 2).HasDefaultValue(100m);
        builder.Property(plan => plan.Features).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(plan => plan.PrioritySupport).HasDefaultValue(false);
        builder.Property(plan => plan.CustomApiKeysAllowed).HasDefaultValue(false);
        builder.Property(plan => plan.IsActive).HasDefaultValue(true);
        builder.Property(plan => plan.SortOrder).HasDefaultValue(0);

        builder.HasIndex(plan => plan.Tier).IsUnique();
        builder.HasIndex(plan => plan.IsActive);
    }
}
