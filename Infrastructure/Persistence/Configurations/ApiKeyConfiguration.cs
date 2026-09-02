using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures API key persistence.
/// </summary>
public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys", table =>
        {
            table.HasCheckConstraint("ck_api_keys_usage_count", "usage_count >= 0");
            table.HasCheckConstraint("ck_api_keys_usage_limit", "usage_limit IS NULL OR usage_limit >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(apiKey => apiKey.ServiceProvider).HasMaxLength(32).IsRequired();
        builder.Property(apiKey => apiKey.KeyIdentifier).HasMaxLength(64).IsRequired();
        builder.Property(apiKey => apiKey.KeyValueEncrypted).HasColumnType("text").IsRequired();
        builder.Property(apiKey => apiKey.IsActive).HasDefaultValue(true);
        builder.Property(apiKey => apiKey.UsageCount).HasDefaultValue(0);
        builder.Property(apiKey => apiKey.LastUsedAtUtc).HasColumnName("last_used_at");
        builder.Property(apiKey => apiKey.ExpiresAtUtc).HasColumnName("expires_at");
        builder.Property(apiKey => apiKey.CreatedByIp).HasMaxLength(45);

        builder.HasIndex(apiKey => apiKey.SellerId);
        builder.HasIndex(apiKey => apiKey.ServiceProvider);
        builder.HasIndex(apiKey => new { apiKey.SellerId, apiKey.ServiceProvider })
            .IsUnique()
            .HasFilter("is_active = TRUE AND deleted_at IS NULL");

        builder.HasOne<Seller>()
            .WithMany(seller => seller.ApiKeys)
            .HasForeignKey(apiKey => apiKey.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
