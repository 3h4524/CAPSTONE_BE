using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures seller profile persistence.
/// </summary>
public sealed class SellerProfileConfiguration : IEntityTypeConfiguration<SellerProfile>
{
    public void Configure(EntityTypeBuilder<SellerProfile> builder)
    {
        builder.ToTable("seller_profiles", table =>
        {
            table.HasCheckConstraint(
                "ck_seller_profiles_completion",
                "profile_completion_percentage BETWEEN 0 AND 100");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(profile => profile.ShopName).HasMaxLength(150);
        builder.Property(profile => profile.ShopDescription).HasColumnType("text");
        builder.Property(profile => profile.NotificationEmailEnabled).HasDefaultValue(true);
        builder.Property(profile => profile.NewsletterSubscribed).HasDefaultValue(false);
        builder.Property(profile => profile.TwoFactorEnabled).HasDefaultValue(false);
        builder.Property(profile => profile.DefaultTimeZone).HasColumnName("default_timezone").HasMaxLength(64).HasDefaultValue("UTC").IsRequired();
        builder.Property(profile => profile.DefaultLanguage).HasMaxLength(16).HasDefaultValue("en").IsRequired();
        builder.Property(profile => profile.ThemePreference).HasMaxLength(24).HasDefaultValue("light").IsRequired();
        builder.Property(profile => profile.ProfileCompletionPercentage).HasPrecision(5, 2).HasDefaultValue(0m);

        builder.HasIndex(profile => profile.SellerId)
            .IsUnique();

        builder.HasOne<Seller>()
            .WithOne(seller => seller.Profile)
            .HasForeignKey<SellerProfile>(profile => profile.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
