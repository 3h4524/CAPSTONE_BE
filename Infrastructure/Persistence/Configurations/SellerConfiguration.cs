using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures the Identity-backed seller aggregate.
/// </summary>
public sealed class SellerConfiguration : IEntityTypeConfiguration<Seller>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Seller> builder)
    {
        builder.ToTable("sellers", table =>
        {
            table.HasCheckConstraint("ck_sellers_account_status", "account_status IN ('active', 'suspended', 'deactivated')");
        });

        builder.Property(seller => seller.Id)
            .UseIdentityByDefaultColumn();

        builder.Property(seller => seller.Email)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(seller => seller.NormalizedEmail)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(seller => seller.UserName)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(seller => seller.NormalizedUserName)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(seller => seller.PasswordHash)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(seller => seller.SecurityStamp)
            .HasMaxLength(64);

        builder.Property(seller => seller.ConcurrencyStamp)
            .HasMaxLength(64);

        builder.Property(seller => seller.PhoneNumber)
            .HasMaxLength(32);

        builder.Property(seller => seller.FullName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(seller => seller.OAuthGoogleId)
            .HasColumnName("oauth_google_id")
            .HasMaxLength(255);

        builder.Property(seller => seller.OAuthProvider)
            .HasColumnName("oauth_provider")
            .HasMaxLength(32);

        builder.Property(seller => seller.AvatarUrl)
            .HasColumnType("text");

        builder.Property(seller => seller.TimeZone)
            .HasColumnName("timezone")
            .HasMaxLength(64)
            .HasDefaultValue("UTC")
            .IsRequired();

        builder.Property(seller => seller.Language)
            .HasMaxLength(16)
            .HasDefaultValue("en")
            .IsRequired();

        builder.Property(seller => seller.AccountStatus)
            .HasMaxLength(16)
            .HasDefaultValue("active")
            .IsRequired();

        builder.Property(seller => seller.EmailConfirmed)
            .HasColumnName("email_verified")
            .HasDefaultValue(false);

        builder.Property(seller => seller.EmailVerifiedAtUtc)
            .HasColumnName("email_verified_at");

        builder.Property(seller => seller.LastLoginAtUtc)
            .HasColumnName("last_login_at");

        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.HasIndex(seller => seller.Email)
            .HasDatabaseName("ix_sellers_email")
            .IsUnique();

        builder.HasIndex(seller => seller.OAuthGoogleId)
            .HasDatabaseName("ix_sellers_oauth_google_id")
            .IsUnique();

        builder.HasIndex(seller => seller.AccountStatus)
            .HasDatabaseName("ix_sellers_account_status");

        builder.HasIndex(seller => seller.DeletedAtUtc)
            .HasDatabaseName("ix_sellers_deleted_at");
    }
}
