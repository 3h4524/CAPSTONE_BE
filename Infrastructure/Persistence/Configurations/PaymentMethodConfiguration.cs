using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures payment method persistence.
/// </summary>
public sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("payment_methods");
        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(method => method.PaymentType).HasMaxLength(24).IsRequired();
        builder.Property(method => method.StripePaymentMethodId).HasMaxLength(128);
        builder.Property(method => method.PaypalEmailEncrypted).HasColumnType("text");
        builder.Property(method => method.CardLast4Digits).HasColumnName("card_last_4_digits").HasMaxLength(4);
        builder.Property(method => method.CardBrand).HasMaxLength(32);
        builder.Property(method => method.IsDefault).HasDefaultValue(false);
        builder.Property(method => method.IsActive).HasDefaultValue(true);

        builder.HasIndex(method => method.SellerId);
        builder.HasIndex(method => method.SellerId)
            .IsUnique()
            .HasFilter("is_default = TRUE AND is_active = TRUE AND deleted_at IS NULL")
            .HasDatabaseName("ix_payment_methods_one_default_per_seller");

        builder.HasOne<Seller>()
            .WithMany(seller => seller.PaymentMethods)
            .HasForeignKey(method => method.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
