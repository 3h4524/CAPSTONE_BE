using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// The outcome states shared by marketplace upload logs.
/// </summary>
internal static class UploadStatuses
{
    public const string Sql = "('pending', 'success', 'failed', 'retrying')";
}

/// <summary>
/// Configures connected Printify shops.
/// </summary>
public sealed class PrintifyIntegrationConfiguration : IEntityTypeConfiguration<PrintifyIntegration>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PrintifyIntegration> builder)
    {
        builder.ToTable("printify_integrations");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(integration => integration.PrintifyStoreId).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(integration => integration.PrintifyApiTokenEncrypted).HasColumnType("text").IsRequired();
        builder.Property(integration => integration.ShopName).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(integration => integration.ShopTitle).HasMaxLength(ColumnLengths.Name);
        builder.Property(integration => integration.IsDefault).HasDefaultValue(false);
        builder.Property(integration => integration.TotalProductsUploaded).HasDefaultValue(0);
        builder.Property(integration => integration.LastSyncAtUtc).HasColumnName("last_sync_at");
        builder.Property(integration => integration.IsActive).HasDefaultValue(true);

        builder.HasIndex(integration => integration.UserId);
        builder.HasIndex(integration => integration.PrintifyStoreId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(integration => integration.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures Printify upload attempts.
/// </summary>
public sealed class PrintifyUploadLogConfiguration : IEntityTypeConfiguration<PrintifyUploadLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PrintifyUploadLog> builder)
    {
        builder.ToTable("printify_upload_logs", table =>
        {
            table.HasCheckConstraint("chk_printify_sync_type", "sync_type IN ('create', 'update')");
            table.HasCheckConstraint("chk_printify_upload_status", $"upload_status IN {UploadStatuses.Sql}");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(log => log.IdempotencyKey).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(log => log.PrintifyProductId).HasMaxLength(ColumnLengths.Name);
        builder.Property(log => log.SyncType).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(log => log.UploadPayload).HasColumnType("jsonb").IsRequired();
        builder.Property(log => log.ApiResponse).HasColumnType("jsonb");
        builder.Property(log => log.UploadStatus).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(log => log.ErrorMessage).HasColumnType("text");
        builder.Property(log => log.RetryCount).HasDefaultValue(0);
        builder.Property(log => log.AttemptedAtUtc).HasColumnName("attempted_at").IsRequired();
        builder.Property(log => log.CompletedAtUtc).HasColumnName("completed_at");

        // Makes a retried upload safe: the same key cannot create a second marketplace product.
        builder.HasIndex(log => new { log.PrintifyIntegrationId, log.IdempotencyKey })
            .HasDatabaseName("uq_printify_idempotency")
            .IsUnique();

        builder.HasIndex(log => log.PrintifyIntegrationId);
        builder.HasIndex(log => log.UploadStatus);
        builder.HasIndex(log => log.ProductId);

        builder.HasQueryFilter(log => log.PrintifyIntegration.DeletedAtUtc == null);

        builder.HasOne(log => log.PrintifyIntegration)
            .WithMany(integration => integration.UploadLogs)
            .HasForeignKey(log => log.PrintifyIntegrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(log => log.Product)
            .WithMany()
            .HasForeignKey(log => log.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BatchJob>()
            .WithMany()
            .HasForeignKey(log => log.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures connected Etsy shops.
/// </summary>
public sealed class EtsyIntegrationConfiguration : IEntityTypeConfiguration<EtsyIntegration>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EtsyIntegration> builder)
    {
        builder.ToTable("etsy_integrations");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(integration => integration.EtsyShopId).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(integration => integration.EtsyOAuthTokenEncrypted)
            .HasColumnName("etsy_oauth_token_encrypted")
            .HasColumnType("text")
            .IsRequired();
        builder.Property(integration => integration.EtsyRefreshTokenEncrypted).HasColumnType("text");
        builder.Property(integration => integration.ShopName).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(integration => integration.IsDefault).HasDefaultValue(false);
        builder.Property(integration => integration.TotalListingsCreated).HasDefaultValue(0);
        builder.Property(integration => integration.TotalListingsUpdated).HasDefaultValue(0);
        builder.Property(integration => integration.LastSyncAtUtc).HasColumnName("last_sync_at");
        builder.Property(integration => integration.OAuthExpiresAtUtc).HasColumnName("oauth_expires_at");
        builder.Property(integration => integration.IsActive).HasDefaultValue(true);

        builder.HasIndex(integration => integration.UserId);
        builder.HasIndex(integration => integration.EtsyShopId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(integration => integration.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures Etsy upload attempts.
/// </summary>
public sealed class EtsyUploadLogConfiguration : IEntityTypeConfiguration<EtsyUploadLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EtsyUploadLog> builder)
    {
        builder.ToTable("etsy_upload_logs", table =>
        {
            table.HasCheckConstraint("chk_etsy_upload_status", $"upload_status IN {UploadStatuses.Sql}");
            table.HasCheckConstraint("chk_etsy_listing_state", "listing_state IN ('draft', 'active')");

            // Going straight to a live listing needs the seller to have said so explicitly.
            table.HasCheckConstraint(
                "chk_etsy_publish_needs_confirmation",
                "publish_immediately = false OR confirmed_by_user_at IS NOT NULL");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(log => log.IdempotencyKey).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(log => log.EtsyListingId).HasMaxLength(ColumnLengths.Name);
        builder.Property(log => log.UploadStatus).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(log => log.ListingState).HasMaxLength(ColumnLengths.Code).HasDefaultValue("draft").IsRequired();
        builder.Property(log => log.PublishImmediately).HasDefaultValue(false).IsRequired();
        builder.Property(log => log.ConfirmedByUserAtUtc).HasColumnName("confirmed_by_user_at");
        builder.Property(log => log.UploadPayload).HasColumnType("jsonb").IsRequired();
        builder.Property(log => log.ApiResponse).HasColumnType("jsonb");
        builder.Property(log => log.ErrorMessage).HasColumnType("text");
        builder.Property(log => log.RetryCount).HasDefaultValue(0);
        builder.Property(log => log.AttemptedAtUtc).HasColumnName("attempted_at").IsRequired();
        builder.Property(log => log.CompletedAtUtc).HasColumnName("completed_at");

        builder.HasIndex(log => new { log.EtsyIntegrationId, log.IdempotencyKey })
            .HasDatabaseName("uq_etsy_idempotency")
            .IsUnique();

        builder.HasIndex(log => log.EtsyIntegrationId);
        builder.HasIndex(log => log.UploadStatus);
        builder.HasIndex(log => log.ProductId);

        builder.HasQueryFilter(log => log.EtsyIntegration.DeletedAtUtc == null);

        builder.HasOne(log => log.EtsyIntegration)
            .WithMany(integration => integration.UploadLogs)
            .HasForeignKey(log => log.EtsyIntegrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(log => log.Product)
            .WithMany()
            .HasForeignKey(log => log.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BatchJob>()
            .WithMany()
            .HasForeignKey(log => log.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures social media shares.
/// </summary>
public sealed class SocialMediaShareConfiguration : IEntityTypeConfiguration<SocialMediaShare>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SocialMediaShare> builder)
    {
        builder.ToTable("social_media_shares", table =>
        {
            table.HasCheckConstraint(
                "chk_social_share_status",
                "share_status IN ('pending', 'scheduled', 'posted', 'failed')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(share => share.Platform).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(share => share.ShareStatus).HasMaxLength(ColumnLengths.Code).HasDefaultValue("pending").IsRequired();
        builder.Property(share => share.ScheduledTimeUtc).HasColumnName("scheduled_time");
        builder.Property(share => share.PostedTimeUtc).HasColumnName("posted_time");
        builder.Property(share => share.PostCaption).HasColumnType("text");
        builder.Property(share => share.ExternalPostId).HasMaxLength(ColumnLengths.Name);
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
            .WithMany(video => video.Shares)
            .HasForeignKey(share => share.PromoVideoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures the hashtags attached to a share.
/// </summary>
public sealed class ShareHashtagConfiguration : IEntityTypeConfiguration<ShareHashtag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ShareHashtag> builder)
    {
        builder.ToTable("share_hashtags");

        builder.ConfigureGeneratedId();

        builder.Property(hashtag => hashtag.Hashtag).HasMaxLength(ColumnLengths.LongCode).IsRequired();

        builder.HasIndex(hashtag => new { hashtag.SocialMediaShareId, hashtag.Position })
            .HasDatabaseName("uq_share_hashtag_position")
            .IsUnique();

        builder.HasQueryFilter(hashtag => hashtag.SocialMediaShare.DeletedAtUtc == null);

        builder.HasOne(hashtag => hashtag.SocialMediaShare)
            .WithMany(share => share.Hashtags)
            .HasForeignKey(hashtag => hashtag.SocialMediaShareId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
