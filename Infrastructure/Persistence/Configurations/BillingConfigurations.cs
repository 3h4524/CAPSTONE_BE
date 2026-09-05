using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures subscription plan persistence.
/// </summary>
public sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("subscription_plans", table =>
        {
            table.HasCheckConstraint("chk_subscription_plans_monthly_price", "monthly_price_usd >= 0");
            table.HasCheckConstraint("chk_subscription_plans_annual_price", "annual_price_usd IS NULL OR annual_price_usd >= 0");
            table.HasCheckConstraint(
                "chk_subscription_plans_quotas",
                "max_batch_size >= 0 AND max_products_per_month >= 0 AND max_concurrent_jobs >= 0 AND image_generation_quota >= 0 AND video_generation_quota >= 0 AND api_call_quota >= 0 AND storage_quota_gb >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(plan => plan.Name).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(plan => plan.Tier).HasMaxLength(ColumnLengths.Code).IsRequired();
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
        builder.Property(plan => plan.PrioritySupport).HasDefaultValue(false);
        builder.Property(plan => plan.CustomApiKeysAllowed).HasDefaultValue(false);
        builder.Property(plan => plan.IsActive).HasDefaultValue(true);
        builder.Property(plan => plan.SortOrder).HasDefaultValue(0);

        builder.HasIndex(plan => plan.Tier).IsUnique();
        builder.HasIndex(plan => plan.IsActive);
    }
}

/// <summary>
/// Configures the feature flags attached to a plan.
/// </summary>
public sealed class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlanFeature> builder)
    {
        builder.ToTable("plan_features");

        builder.HasKey(feature => new { feature.PlanId, feature.FeatureCode });

        builder.Property(feature => feature.FeatureCode)
            .HasMaxLength(ColumnLengths.LongCode)
            .IsRequired();

        builder.Property(feature => feature.IsEnabled).HasDefaultValue(true);

        builder.Property(feature => feature.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasOne(feature => feature.Plan)
            .WithMany(plan => plan.Features)
            .HasForeignKey(feature => feature.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures subscription persistence.
/// </summary>
public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions", table =>
        {
            table.HasCheckConstraint("chk_subscriptions_billing_cycle", "billing_cycle IN ('monthly', 'annual')");
            table.HasCheckConstraint("chk_subscriptions_status", "status IN ('trialing', 'active', 'past_due', 'cancelled', 'expired')");
            table.HasCheckConstraint("chk_subscriptions_date_order", "renewal_date >= start_date");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(subscription => subscription.BillingCycle).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(subscription => subscription.Status).HasMaxLength(ColumnLengths.Code).HasDefaultValue("active").IsRequired();
        builder.Property(subscription => subscription.MonthlyPriceUsd).HasPrecision(10, 2).IsRequired();
        builder.Property(subscription => subscription.AnnualPriceUsd).HasPrecision(10, 2);
        builder.Property(subscription => subscription.AutoRenew).HasDefaultValue(true);

        builder.HasIndex(subscription => subscription.UserId);
        builder.HasIndex(subscription => subscription.Status);
        builder.HasIndex(subscription => subscription.RenewalDate);
        builder.HasIndex(subscription => subscription.CreatedAtUtc);

        // Restrict, not cascade: a user carrying financial records must fail deletion cleanly
        // rather than part-way through, and soft delete is the supported path anyway.
        builder.HasOne<User>()
            .WithMany(user => user.Subscriptions)
            .HasForeignKey(subscription => subscription.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(subscription => subscription.Plan)
            .WithMany(plan => plan.Subscriptions)
            .HasForeignKey(subscription => subscription.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Configures stored payment methods.
/// </summary>
public sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("payment_methods");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(method => method.PaymentType).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(method => method.StripePaymentMethodId).HasMaxLength(ColumnLengths.Name);
        builder.Property(method => method.PaypalEmailEncrypted).HasColumnType("text");
        builder.Property(method => method.CardLast4Digits).HasMaxLength(4);
        builder.Property(method => method.CardBrand).HasMaxLength(ColumnLengths.Code);
        builder.Property(method => method.IsDefault).HasDefaultValue(false);
        builder.Property(method => method.IsActive).HasDefaultValue(true);

        builder.HasIndex(method => method.UserId);

        builder.HasOne<User>()
            .WithMany(user => user.PaymentMethods)
            .HasForeignKey(method => method.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Configures invoice persistence.
/// </summary>
public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", table =>
        {
            table.HasCheckConstraint("chk_invoices_status", "status IN ('draft', 'issued', 'paid', 'overdue', 'void', 'refunded')");
            table.HasCheckConstraint("chk_invoices_total", "total_amount = amount_usd + tax_amount");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(invoice => invoice.AmountUsd).HasPrecision(10, 2).IsRequired();
        builder.Property(invoice => invoice.TaxAmount).HasPrecision(10, 2).HasDefaultValue(0m);
        builder.Property(invoice => invoice.TotalAmount).HasPrecision(10, 2).IsRequired();
        builder.Property(invoice => invoice.Status).HasMaxLength(ColumnLengths.Code).HasDefaultValue("draft").IsRequired();
        builder.Property(invoice => invoice.Items).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(invoice => invoice.PdfUrl).HasColumnType("text");
        builder.Property(invoice => invoice.StripeInvoiceId).HasMaxLength(ColumnLengths.Name);

        builder.HasIndex(invoice => invoice.InvoiceNumber).IsUnique();
        builder.HasIndex(invoice => invoice.UserId);
        builder.HasIndex(invoice => invoice.Status);
        builder.HasIndex(invoice => invoice.CreatedAtUtc);

        builder.HasQueryFilter(invoice => invoice.Subscription.DeletedAtUtc == null);

        builder.HasOne(invoice => invoice.Subscription)
            .WithMany(subscription => subscription.Invoices)
            .HasForeignKey(invoice => invoice.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany(user => user.Invoices)
            .HasForeignKey(invoice => invoice.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Configures per-period usage aggregates.
/// </summary>
public sealed class UsageStatisticConfiguration : IEntityTypeConfiguration<UsageStatistic>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UsageStatistic> builder)
    {
        builder.ToTable("usage_statistics");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(statistic => statistic.TotalApiCostUsd).HasPrecision(12, 6).HasDefaultValue(0m);
        builder.Property(statistic => statistic.StorageUsedGb).HasPrecision(10, 2).HasDefaultValue(0m);
        builder.Property(statistic => statistic.AverageProcessingTimeSeconds).HasPrecision(10, 2).HasDefaultValue(0m);
        builder.Property(statistic => statistic.ImagesGenerated).HasDefaultValue(0);
        builder.Property(statistic => statistic.VideosCreated).HasDefaultValue(0);
        builder.Property(statistic => statistic.ListingsExported).HasDefaultValue(0);
        builder.Property(statistic => statistic.TotalApiCalls).HasDefaultValue(0);
        builder.Property(statistic => statistic.BatchJobsCompleted).HasDefaultValue(0);

        builder.HasIndex(statistic => new { statistic.UserId, statistic.BillingPeriodStart, statistic.BillingPeriodEnd })
            .HasDatabaseName("uq_usage_statistics_period")
            .IsUnique();

        builder.HasOne<User>()
            .WithMany(user => user.UsageStatistics)
            .HasForeignKey(statistic => statistic.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Configures the billable provider-call ledger.
/// </summary>
public sealed class ApiUsageRecordConfiguration : IEntityTypeConfiguration<ApiUsageRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApiUsageRecord> builder)
    {
        builder.ToTable("api_usage_records", table =>
        {
            table.HasCheckConstraint(
                "chk_api_usage_provider",
                "provider IN ('leonardo', 'stable_diffusion', 'openai', 'gemini', 'printify', 'etsy', 'ffmpeg_local')");
            table.HasCheckConstraint(
                "chk_api_usage_feature",
                "feature IN ('image_generation', 'mockup', 'video', 'listing', 'seo', 'integration')");
            table.HasCheckConstraint("chk_api_usage_status", "status IN ('success', 'failed', 'timeout')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(record => record.Provider).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(record => record.Feature).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(record => record.ModelName).HasMaxLength(ColumnLengths.LongCode);
        builder.Property(record => record.ProviderRequestId).HasMaxLength(ColumnLengths.Name);
        builder.Property(record => record.RequestUnits).HasDefaultValue(1);
        builder.Property(record => record.CostUsd).HasPrecision(12, 6).HasDefaultValue(0m);
        builder.Property(record => record.Status).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(record => record.ErrorCode).HasMaxLength(ColumnLengths.LongCode);

        builder.HasIndex(record => new { record.UserId, record.CreatedAtUtc });
        builder.HasIndex(record => record.BatchJobId);
        builder.HasIndex(record => new { record.Provider, record.Feature });
        builder.HasIndex(record => record.ProviderRequestId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(record => record.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(record => record.BatchJob)
            .WithMany()
            .HasForeignKey(record => record.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(record => record.Product)
            .WithMany()
            .HasForeignKey(record => record.ProductId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
