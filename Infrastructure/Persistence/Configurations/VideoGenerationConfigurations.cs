using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures video template persistence.
/// </summary>
public sealed class VideoTemplateConfiguration : IEntityTypeConfiguration<VideoTemplate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VideoTemplate> builder)
    {
        builder.ToTable("video_templates", table =>
        {
            table.HasCheckConstraint("ck_video_templates_duration", "duration_seconds > 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(template => template.Name).HasMaxLength(150).IsRequired();
        builder.Property(template => template.Type).HasMaxLength(64).IsRequired();
        builder.Property(template => template.Platform).HasMaxLength(32).IsRequired();
        builder.Property(template => template.AspectRatio).HasMaxLength(16).IsRequired();
        builder.Property(template => template.Resolution).HasMaxLength(24).IsRequired();
        builder.Property(template => template.EffectsConfig).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(template => template.PreviewVideoUrl).HasColumnType("text");
        builder.Property(template => template.IsSystemTemplate).HasDefaultValue(true);
        builder.Property(template => template.IsActive).HasDefaultValue(true);

        builder.HasIndex(template => template.Type);
        builder.HasIndex(template => template.Platform);
    }
}

/// <summary>
/// Configures music track persistence.
/// </summary>
public sealed class MusicTrackConfiguration : IEntityTypeConfiguration<MusicTrack>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MusicTrack> builder)
    {
        builder.ToTable("music_tracks", table =>
        {
            table.HasCheckConstraint("ck_music_tracks_duration", "duration_seconds > 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(track => track.Name).HasMaxLength(150).IsRequired();
        builder.Property(track => track.ArtistName).HasMaxLength(150).IsRequired();
        builder.Property(track => track.Genre).HasMaxLength(64).IsRequired();
        builder.Property(track => track.Mood).HasMaxLength(64).IsRequired();
        builder.Property(track => track.RoyaltyFree).HasDefaultValue(true);
        builder.Property(track => track.LicenseType).HasMaxLength(64).IsRequired();
        builder.Property(track => track.AudioUrl).HasColumnType("text").IsRequired();
        builder.Property(track => track.PreviewUrl).HasColumnType("text");
        builder.Property(track => track.WaveformData).HasColumnType("jsonb");
        builder.Property(track => track.IsAvailable).HasDefaultValue(true);

        builder.HasIndex(track => track.Genre);
        builder.HasIndex(track => track.Mood);
    }
}

/// <summary>
/// Configures promotional video persistence.
/// </summary>
public sealed class PromoVideoConfiguration : IEntityTypeConfiguration<PromoVideo>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PromoVideo> builder)
    {
        builder.ToTable("promo_videos", table =>
        {
            table.HasCheckConstraint("ck_promo_videos_duration", "video_duration_seconds > 0 AND (generation_time_seconds IS NULL OR generation_time_seconds >= 0)");
            table.HasCheckConstraint("ck_promo_videos_file_size", "file_size_mb IS NULL OR file_size_mb >= 0");
            table.HasCheckConstraint("ck_promo_videos_quality", "quality_score IS NULL OR quality_score BETWEEN 0 AND 1");
            table.HasCheckConstraint("ck_promo_videos_rating", "user_rating IS NULL OR user_rating BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_promo_videos_cost", "api_cost_usd >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();
        builder.HasQueryFilter(video => video.DeletedAtUtc == null && video.Product.DeletedAtUtc == null);

        builder.Property(video => video.DesignImageIds).HasColumnType("integer[]").IsRequired();
        builder.Property(video => video.TextOverlayContent).HasColumnType("text");
        builder.Property(video => video.TextOverlayColor).HasMaxLength(7);
        builder.Property(video => video.TextOverlayFont).HasMaxLength(64);
        builder.Property(video => video.VideoUrl).HasColumnType("text");
        builder.Property(video => video.VideoLocalPath).HasMaxLength(500);
        builder.Property(video => video.VideoResolution).HasMaxLength(24).IsRequired();
        builder.Property(video => video.FileFormat).HasMaxLength(16).IsRequired();
        builder.Property(video => video.AspectRatio).HasMaxLength(16).IsRequired();
        builder.Property(video => video.FileSizeMb).HasPrecision(10, 2);
        builder.Property(video => video.PlatformTarget).HasMaxLength(32).IsRequired();
        builder.Property(video => video.QualityScore).HasPrecision(3, 2);
        builder.Property(video => video.Status).HasMaxLength(24).HasDefaultValue("pending").IsRequired();
        builder.Property(video => video.ApprovalStatus).HasMaxLength(24).HasDefaultValue("pending").IsRequired();
        builder.Property(video => video.IsFinal).HasDefaultValue(false);
        builder.Property(video => video.GenerationTimeSeconds).HasPrecision(10, 2);
        builder.Property(video => video.ApiCostUsd).HasPrecision(10, 4).HasDefaultValue(0m);

        builder.HasIndex(video => video.ProductId);
        builder.HasIndex(video => video.Status);
        builder.HasIndex(video => video.ApprovalStatus);
        builder.HasIndex(video => video.CreatedAtUtc).IsDescending();

        builder.HasOne(video => video.Product)
            .WithMany(product => product.PromoVideos)
            .HasForeignKey(video => video.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(video => video.BatchJob)
            .WithMany()
            .HasForeignKey(video => video.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(video => video.VideoTemplate)
            .WithMany(template => template.PromoVideos)
            .HasForeignKey(video => video.VideoTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(video => video.MusicTrack)
            .WithMany(track => track.PromoVideos)
            .HasForeignKey(video => video.MusicTrackId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
