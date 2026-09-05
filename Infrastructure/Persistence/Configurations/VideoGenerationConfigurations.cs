using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures promotional video templates.
/// </summary>
public sealed class VideoTemplateConfiguration : IEntityTypeConfiguration<VideoTemplate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VideoTemplate> builder)
    {
        builder.ToTable("video_templates", table =>
        {
            table.HasCheckConstraint(
                "chk_video_templates_type",
                "type IN ('slideshow', 'product_showcase', 'lifestyle_reel', 'story_vertical')");
            table.HasCheckConstraint("chk_video_templates_duration", "duration_seconds BETWEEN 15 AND 30");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(template => template.Name).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(template => template.Type).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(template => template.Platform).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(template => template.AspectRatio).HasMaxLength(20).IsRequired();
        builder.Property(template => template.Resolution).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(template => template.EffectsConfig).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(template => template.PreviewVideoUrl).HasColumnType("text");
        builder.Property(template => template.IsSystemTemplate).HasDefaultValue(true);
        builder.Property(template => template.IsActive).HasDefaultValue(true);

        builder.HasIndex(template => template.Type);
        builder.HasIndex(template => template.Platform);
    }
}

/// <summary>
/// Configures licensed music tracks.
/// </summary>
public sealed class MusicTrackConfiguration : IEntityTypeConfiguration<MusicTrack>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MusicTrack> builder)
    {
        builder.ToTable("music_tracks", table =>
        {
            // A royalty-free claim has to say where the licence came from.
            table.HasCheckConstraint(
                "chk_music_tracks_license_source",
                "royalty_free = false OR license_source IS NOT NULL");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(track => track.Name).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(track => track.ArtistName).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(track => track.Genre).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(track => track.Mood).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(track => track.RoyaltyFree).HasDefaultValue(false).IsRequired();
        builder.Property(track => track.LicenseType).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(track => track.LicenseSource).HasMaxLength(ColumnLengths.Name);
        builder.Property(track => track.AudioUrl).HasColumnType("text").IsRequired();
        builder.Property(track => track.PreviewUrl).HasColumnType("text");
        builder.Property(track => track.IsAvailable).HasDefaultValue(true);

        builder.HasIndex(track => track.Genre);
        builder.HasIndex(track => track.Mood);
    }
}

/// <summary>
/// Configures rendered promotional videos.
/// </summary>
public sealed class PromoVideoConfiguration : IEntityTypeConfiguration<PromoVideo>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PromoVideo> builder)
    {
        builder.ToTable("promo_videos", table =>
        {
            table.HasCheckConstraint("chk_promo_videos_duration", "video_duration_seconds BETWEEN 15 AND 30");
            table.HasCheckConstraint("chk_promo_videos_status", "status IN ('pending', 'queued', 'rendering', 'completed', 'failed')");
            table.HasCheckConstraint("chk_promo_videos_approval", $"approval_status IN {ApprovalStatuses.Sql}");
            table.HasCheckConstraint("chk_promo_videos_rating", "user_rating IS NULL OR user_rating BETWEEN 1 AND 5");
            table.HasCheckConstraint("chk_promo_videos_quality_score", "quality_score IS NULL OR quality_score BETWEEN 0 AND 1");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(video => video.TextOverlayContent).HasColumnType("text");
        builder.Property(video => video.TextOverlayColor).HasMaxLength(7);
        builder.Property(video => video.TextOverlayFont).HasMaxLength(ColumnLengths.LongCode);
        builder.Property(video => video.StorageProvider).HasMaxLength(ColumnLengths.Code).HasDefaultValue("s3");
        builder.Property(video => video.StorageKey).HasMaxLength(ColumnLengths.StorageKey);
        builder.Property(video => video.VideoUrl).HasColumnType("text");
        builder.Property(video => video.VideoResolution).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(video => video.FileFormat).HasMaxLength(10).IsRequired();
        builder.Property(video => video.AspectRatio).HasMaxLength(20).IsRequired();
        builder.Property(video => video.FileSizeMb).HasPrecision(10, 2);
        builder.Property(video => video.PlatformTarget).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(video => video.QualityScore).HasPrecision(3, 2);
        builder.Property(video => video.Status).HasMaxLength(ColumnLengths.Code).HasDefaultValue("pending").IsRequired();
        builder.Property(video => video.ApprovalStatus).HasMaxLength(ColumnLengths.Code).HasDefaultValue("pending").IsRequired();
        builder.Property(video => video.IsFinal).HasDefaultValue(false);
        builder.Property(video => video.GenerationTimeSeconds).HasPrecision(10, 2);

        builder.HasIndex(video => video.ProductId);
        builder.HasIndex(video => video.Status);
        builder.HasIndex(video => video.ApprovalStatus);
        builder.HasIndex(video => video.CreatedAtUtc);

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
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(video => video.ApiUsageRecord)
            .WithMany()
            .HasForeignKey(video => video.ApiUsageRecordId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures the ordered scenes inside a promotional video.
/// </summary>
public sealed class PromoVideoSceneConfiguration : IEntityTypeConfiguration<PromoVideoScene>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PromoVideoScene> builder)
    {
        builder.ToTable("promo_video_scenes", table =>
        {
            // Exactly one asset per scene: a design or a mockup, never both, never neither.
            table.HasCheckConstraint(
                "chk_scene_exactly_one_asset",
                "(design_image_id IS NOT NULL) <> (mockup_image_id IS NOT NULL)");
            table.HasCheckConstraint("chk_scene_order_positive", "scene_order > 0");
            table.HasCheckConstraint(
                "chk_scene_transition",
                "transition_effect IS NULL OR transition_effect IN ('fade', 'slide', 'zoom', 'ken_burns', 'none')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(scene => scene.DurationSeconds).HasPrecision(5, 2).HasDefaultValue(3m).IsRequired();
        builder.Property(scene => scene.TransitionEffect).HasMaxLength(ColumnLengths.Code);
        builder.Property(scene => scene.TextOverlayContent).HasColumnType("text");

        builder.HasIndex(scene => new { scene.PromoVideoId, scene.SceneOrder })
            .HasDatabaseName("uq_promo_video_scene_order")
            .IsUnique();

        builder.HasIndex(scene => scene.DesignImageId);
        builder.HasIndex(scene => scene.MockupImageId);

        builder.HasQueryFilter(scene => scene.PromoVideo.DeletedAtUtc == null);

        builder.HasOne(scene => scene.PromoVideo)
            .WithMany(video => video.Scenes)
            .HasForeignKey(scene => scene.PromoVideoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(scene => scene.DesignImage)
            .WithMany(image => image.PromoVideoScenes)
            .HasForeignKey(scene => scene.DesignImageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(scene => scene.MockupImage)
            .WithMany(image => image.PromoVideoScenes)
            .HasForeignKey(scene => scene.MockupImageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
