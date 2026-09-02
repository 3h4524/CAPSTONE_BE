using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

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
            table.HasCheckConstraint("ck_export_packages_file_size", "file_size_mb IS NULL OR file_size_mb >= 0");
            table.HasCheckConstraint("ck_export_packages_creation_time", "creation_time_seconds >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.HasQueryFilter(package => package.BatchJob.DeletedAtUtc == null);

        builder.Property(package => package.ProductIds).HasColumnType("integer[]").IsRequired();
        builder.Property(package => package.Type).HasMaxLength(32).IsRequired();
        builder.Property(package => package.Name).HasMaxLength(150).IsRequired();
        builder.Property(package => package.Description).HasColumnType("text");
        builder.Property(package => package.PackageContent).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(package => package.FilePath).HasMaxLength(500);
        builder.Property(package => package.DownloadUrl).HasColumnType("text");
        builder.Property(package => package.FileSizeMb).HasPrecision(10, 2);
        builder.Property(package => package.CreationTimeSeconds).HasPrecision(10, 2).IsRequired();
        builder.Property(package => package.Status).HasMaxLength(24).HasDefaultValue("preparing").IsRequired();
        builder.Property(package => package.DownloadedAtUtc).HasColumnName("downloaded_at");
        builder.Property(package => package.ExpiresAtUtc).HasColumnName("expires_at");

        builder.HasIndex(package => package.BatchJobId);
        builder.HasIndex(package => package.Status);
        builder.HasIndex(package => package.ExpiresAtUtc);

        builder.HasOne(package => package.BatchJob)
            .WithMany()
            .HasForeignKey(package => package.BatchJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Seller>()
            .WithMany()
            .HasForeignKey(package => package.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures Printify integration persistence.
/// </summary>
public sealed class PrintifyIntegrationConfiguration : IEntityTypeConfiguration<PrintifyIntegration>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PrintifyIntegration> builder)
    {
        builder.ToTable("printify_integrations", table =>
        {
            table.HasCheckConstraint("ck_printify_integrations_total_products", "total_products_uploaded >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(integration => integration.PrintifyStoreId).HasMaxLength(128).IsRequired();
        builder.Property(integration => integration.PrintifyApiTokenEncrypted).HasColumnType("text").IsRequired();
        builder.Property(integration => integration.ShopName).HasMaxLength(150).IsRequired();
        builder.Property(integration => integration.ShopTitle).HasMaxLength(150);
        builder.Property(integration => integration.TotalProductsUploaded).HasDefaultValue(0);
        builder.Property(integration => integration.LastSyncAtUtc).HasColumnName("last_sync_at");
        builder.Property(integration => integration.IsActive).HasDefaultValue(true);

        builder.HasIndex(integration => integration.SellerId).IsUnique();
        builder.HasIndex(integration => integration.PrintifyStoreId).IsUnique();

        builder.HasOne<Seller>()
            .WithOne()
            .HasForeignKey<PrintifyIntegration>(integration => integration.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures Printify upload log persistence.
/// </summary>
public sealed class PrintifyUploadLogConfiguration : IEntityTypeConfiguration<PrintifyUploadLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PrintifyUploadLog> builder)
    {
        builder.ToTable("printify_upload_logs", table =>
        {
            table.HasCheckConstraint("ck_printify_upload_logs_retry_count", "retry_count >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.HasQueryFilter(log => log.PrintifyIntegration.DeletedAtUtc == null && log.Product.DeletedAtUtc == null);

        builder.Property(log => log.PrintifyProductId).HasMaxLength(128);
        builder.Property(log => log.SyncType).HasMaxLength(32).IsRequired();
        builder.Property(log => log.UploadPayload).HasColumnType("jsonb").IsRequired();
        builder.Property(log => log.ApiResponse).HasColumnType("jsonb");
        builder.Property(log => log.UploadStatus).HasMaxLength(24).IsRequired();
        builder.Property(log => log.ErrorMessage).HasColumnType("text");
        builder.Property(log => log.RetryCount).HasDefaultValue(0);
        builder.Property(log => log.AttemptedAtUtc).HasColumnName("attempted_at");
        builder.Property(log => log.CompletedAtUtc).HasColumnName("completed_at");

        builder.HasIndex(log => log.PrintifyIntegrationId);
        builder.HasIndex(log => log.UploadStatus);

        builder.HasOne(log => log.PrintifyIntegration)
            .WithMany(integration => integration.UploadLogs)
            .HasForeignKey(log => log.PrintifyIntegrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(log => log.Product)
            .WithMany()
            .HasForeignKey(log => log.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(log => log.BatchJob)
            .WithMany()
            .HasForeignKey(log => log.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures Etsy integration persistence.
/// </summary>
public sealed class EtsyIntegrationConfiguration : IEntityTypeConfiguration<EtsyIntegration>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EtsyIntegration> builder)
    {
        builder.ToTable("etsy_integrations", table =>
        {
            table.HasCheckConstraint("ck_etsy_integrations_totals", "total_listings_created >= 0 AND total_listings_updated >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(integration => integration.EtsyShopId).HasMaxLength(128).IsRequired();
        builder.Property(integration => integration.EtsyOAuthTokenEncrypted).HasColumnName("etsy_oauth_token_encrypted").HasColumnType("text").IsRequired();
        builder.Property(integration => integration.ShopName).HasMaxLength(150).IsRequired();
        builder.Property(integration => integration.TotalListingsCreated).HasDefaultValue(0);
        builder.Property(integration => integration.TotalListingsUpdated).HasDefaultValue(0);
        builder.Property(integration => integration.LastSyncAtUtc).HasColumnName("last_sync_at");
        builder.Property(integration => integration.OAuthExpiresAtUtc).HasColumnName("oauth_expires_at");
        builder.Property(integration => integration.IsActive).HasDefaultValue(true);

        builder.HasIndex(integration => integration.SellerId).IsUnique();
        builder.HasIndex(integration => integration.EtsyShopId).IsUnique();

        builder.HasOne<Seller>()
            .WithOne()
            .HasForeignKey<EtsyIntegration>(integration => integration.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures Etsy upload log persistence.
/// </summary>
public sealed class EtsyUploadLogConfiguration : IEntityTypeConfiguration<EtsyUploadLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EtsyUploadLog> builder)
    {
        builder.ToTable("etsy_upload_logs", table =>
        {
            table.HasCheckConstraint("ck_etsy_upload_logs_retry_count", "retry_count >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.HasQueryFilter(log => log.EtsyIntegration.DeletedAtUtc == null && log.Product.DeletedAtUtc == null);

        builder.Property(log => log.EtsyListingId).HasMaxLength(128);
        builder.Property(log => log.UploadStatus).HasMaxLength(24).IsRequired();
        builder.Property(log => log.PublishImmediately).HasDefaultValue(false);
        builder.Property(log => log.UploadPayload).HasColumnType("jsonb").IsRequired();
        builder.Property(log => log.ApiResponse).HasColumnType("jsonb");
        builder.Property(log => log.ErrorMessage).HasColumnType("text");
        builder.Property(log => log.RetryCount).HasDefaultValue(0);
        builder.Property(log => log.AttemptedAtUtc).HasColumnName("attempted_at");
        builder.Property(log => log.CompletedAtUtc).HasColumnName("completed_at");

        builder.HasIndex(log => log.EtsyIntegrationId);
        builder.HasIndex(log => log.UploadStatus);

        builder.HasOne(log => log.EtsyIntegration)
            .WithMany(integration => integration.UploadLogs)
            .HasForeignKey(log => log.EtsyIntegrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(log => log.Product)
            .WithMany()
            .HasForeignKey(log => log.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(log => log.BatchJob)
            .WithMany()
            .HasForeignKey(log => log.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures social media share persistence.
/// </summary>
public sealed class SocialMediaShareConfiguration : IEntityTypeConfiguration<SocialMediaShare>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SocialMediaShare> builder)
    {
        builder.ToTable("social_media_shares", table =>
        {
            table.HasCheckConstraint("ck_social_media_shares_retry_count", "retry_count >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();
        builder.HasQueryFilter(share => share.DeletedAtUtc == null && share.Product.DeletedAtUtc == null && share.PromoVideo.DeletedAtUtc == null);

        builder.Property(share => share.Platform).HasMaxLength(32).IsRequired();
        builder.Property(share => share.ShareStatus).HasMaxLength(24).HasDefaultValue("pending").IsRequired();
        builder.Property(share => share.ScheduledTimeUtc).HasColumnName("scheduled_time");
        builder.Property(share => share.PostedTimeUtc).HasColumnName("posted_time");
        builder.Property(share => share.PostCaption).HasColumnType("text");
        builder.Property(share => share.Hashtags).HasColumnType("character varying(100)[]");
        builder.Property(share => share.ExternalPostId).HasMaxLength(128);
        builder.Property(share => share.ExternalPlatformUrl).HasColumnType("text");
        builder.Property(share => share.ErrorMessage).HasColumnType("text");
        builder.Property(share => share.RetryCount).HasDefaultValue(0);
        builder.Property(share => share.ApiResponse).HasColumnType("jsonb");

        builder.HasIndex(share => share.ProductId);
        builder.HasIndex(share => share.Platform);
        builder.HasIndex(share => share.ShareStatus);

        builder.HasOne(share => share.Product)
            .WithMany()
            .HasForeignKey(share => share.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(share => share.PromoVideo)
            .WithMany()
            .HasForeignKey(share => share.PromoVideoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
