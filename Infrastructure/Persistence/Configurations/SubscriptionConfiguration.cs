using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures subscription persistence.
/// </summary>
public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions", table =>
        {
            table.HasCheckConstraint("ck_subscriptions_prices", "monthly_price_usd >= 0 AND (annual_price_usd IS NULL OR annual_price_usd >= 0)");
            table.HasCheckConstraint("ck_subscriptions_dates", "renewal_date >= start_date");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(subscription => subscription.BillingCycle).HasMaxLength(16).IsRequired();
        builder.Property(subscription => subscription.MonthlyPriceUsd).HasPrecision(10, 2).IsRequired();
        builder.Property(subscription => subscription.AnnualPriceUsd).HasPrecision(10, 2);
        builder.Property(subscription => subscription.Status).HasMaxLength(24).HasDefaultValue("active").IsRequired();
        builder.Property(subscription => subscription.TrialEndsAtUtc).HasColumnName("trial_ends_at");
        builder.Property(subscription => subscription.CancelledAtUtc).HasColumnName("cancelled_at");
        builder.Property(subscription => subscription.AutoRenew).HasDefaultValue(true);

        builder.HasIndex(subscription => subscription.SellerId);
        builder.HasIndex(subscription => subscription.Status);
        builder.HasIndex(subscription => subscription.RenewalDate);
        builder.HasIndex(subscription => subscription.CreatedAtUtc).IsDescending();

        builder.HasOne<Seller>()
            .WithMany(seller => seller.Subscriptions)
            .HasForeignKey(subscription => subscription.SellerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(subscription => subscription.Plan)
            .WithMany(plan => plan.Subscriptions)
            .HasForeignKey(subscription => subscription.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
