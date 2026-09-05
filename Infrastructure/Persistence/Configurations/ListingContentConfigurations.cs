using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures generated listing content.
/// </summary>
public sealed class ListingContentConfiguration : IEntityTypeConfiguration<ListingContent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingContent> builder)
    {
        builder.ToTable("listing_contents", table =>
        {
            table.HasCheckConstraint("chk_listing_contents_approval", $"approval_status IN {ApprovalStatuses.Sql}");
            table.HasCheckConstraint(
                "chk_listing_contents_approved_at",
                "approval_status <> 'approved' OR approved_at IS NOT NULL");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(content => content.AiModelUsed).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(content => content.ModelVersion).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(content => content.GenerationTimeSeconds).HasPrecision(10, 2).IsRequired();
        builder.Property(content => content.ApprovalStatus).HasMaxLength(ColumnLengths.Code).HasDefaultValue("pending").IsRequired();
        builder.Property(content => content.ApprovedAtUtc).HasColumnName("approved_at");

        builder.HasIndex(content => content.ProductId).IsUnique();
        builder.HasIndex(content => content.ApprovalStatus);

        // Listing content and everything beneath it disappears with its product.
        builder.HasQueryFilter(content => content.Product.DeletedAtUtc == null);

        builder.HasOne(content => content.Product)
            .WithOne(product => product.ListingContent)
            .HasForeignKey<ListingContent>(content => content.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(content => content.BatchJob)
            .WithMany()
            .HasForeignKey(content => content.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures listing title versions.
/// </summary>
public sealed class ListingTitleConfiguration : IEntityTypeConfiguration<ListingTitle>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingTitle> builder)
    {
        builder.ToTable("listing_titles", table =>
        {
            // Both columns are derived from current_title, so the database refuses a write that
            // updates the title without recomputing them.
            table.HasCheckConstraint("chk_listing_titles_char_count", "character_count = LENGTH(current_title)");
            table.HasCheckConstraint(
                "chk_listing_titles_edited_flag",
                "is_user_edited = (current_title IS DISTINCT FROM ai_generated_title)");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(title => title.AiGeneratedTitle).HasMaxLength(ColumnLengths.ListingTitle).IsRequired();
        builder.Property(title => title.CurrentTitle).HasMaxLength(ColumnLengths.ListingTitle).IsRequired();
        builder.Property(title => title.IsUserEdited).HasDefaultValue(false).IsRequired();
        builder.Property(title => title.SeoScore).HasPrecision(5, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(title => title.VersionNumber).HasDefaultValue(1);

        builder.HasIndex(title => new { title.ListingContentId, title.VersionNumber })
            .HasDatabaseName("uq_listing_titles_version")
            .IsUnique();

        builder.HasQueryFilter(title => title.ListingContent.Product.DeletedAtUtc == null);

        builder.HasOne(title => title.ListingContent)
            .WithMany(content => content.Titles)
            .HasForeignKey(title => title.ListingContentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures listing tag-set versions.
/// </summary>
public sealed class ListingTagConfiguration : IEntityTypeConfiguration<ListingTag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingTag> builder)
    {
        builder.ToTable("listing_tags");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(tag => tag.TagTypeDistribution).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(tag => tag.SeoScore).HasPrecision(5, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(tag => tag.IsUserEdited).HasDefaultValue(false).IsRequired();
        builder.Property(tag => tag.VersionNumber).HasDefaultValue(1);

        builder.HasIndex(tag => new { tag.ListingContentId, tag.VersionNumber })
            .HasDatabaseName("uq_listing_tags_version")
            .IsUnique();

        builder.HasQueryFilter(tag => tag.ListingContent.Product.DeletedAtUtc == null);

        builder.HasOne(tag => tag.ListingContent)
            .WithMany(content => content.Tags)
            .HasForeignKey(tag => tag.ListingContentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures the individual tags inside a tag set.
/// </summary>
public sealed class ListingTagItemConfiguration : IEntityTypeConfiguration<ListingTagItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingTagItem> builder)
    {
        builder.ToTable("listing_tag_items", table =>
        {
            table.HasCheckConstraint("chk_tag_position_range", "position BETWEEN 1 AND 13");
            table.HasCheckConstraint(
                "chk_tag_type",
                "tag_type IN ('primary', 'long_tail', 'niche', 'occasion', 'product_type')");
            table.HasCheckConstraint("chk_tag_source", "source IN ('ai', 'user')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(item => item.TagValue).HasMaxLength(ColumnLengths.Tag).IsRequired();
        builder.Property(item => item.NormalizedValue).HasMaxLength(ColumnLengths.Tag).IsRequired();
        builder.Property(item => item.TagType).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(item => item.Source).HasMaxLength(ColumnLengths.Tag).HasDefaultValue("ai").IsRequired();

        builder.HasIndex(item => new { item.ListingTagId, item.Position })
            .HasDatabaseName("uq_listing_tag_position")
            .IsUnique();

        builder.HasIndex(item => new { item.ListingTagId, item.NormalizedValue })
            .HasDatabaseName("uq_listing_tag_no_duplicate")
            .IsUnique();

        builder.HasIndex(item => item.NormalizedValue);

        builder.HasQueryFilter(item => item.ListingTag.ListingContent.Product.DeletedAtUtc == null);

        builder.HasOne(item => item.ListingTag)
            .WithMany(tag => tag.Items)
            .HasForeignKey(item => item.ListingTagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures listing description versions.
/// </summary>
public sealed class ListingDescriptionConfiguration : IEntityTypeConfiguration<ListingDescription>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingDescription> builder)
    {
        builder.ToTable("listing_descriptions");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(description => description.AiGeneratedDescription).HasColumnType("text").IsRequired();
        builder.Property(description => description.CurrentDescription).HasColumnType("text").IsRequired();
        builder.Property(description => description.IsUserEdited).HasDefaultValue(false).IsRequired();
        builder.Property(description => description.StructureFollowed).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(description => description.SeoScore).HasPrecision(5, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(description => description.VersionNumber).HasDefaultValue(1);

        builder.HasIndex(description => new { description.ListingContentId, description.VersionNumber })
            .HasDatabaseName("uq_listing_descriptions_version")
            .IsUnique();

        builder.HasQueryFilter(description => description.ListingContent.Product.DeletedAtUtc == null);

        builder.HasOne(description => description.ListingContent)
            .WithMany(content => content.Descriptions)
            .HasForeignKey(description => description.ListingContentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures the computed SEO assessment.
/// </summary>
public sealed class SeoScoreConfiguration : IEntityTypeConfiguration<SeoScore>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SeoScore> builder)
    {
        builder.ToTable("seo_scores", table =>
        {
            table.HasCheckConstraint("chk_seo_scores_range", "overall_seo_score BETWEEN 0 AND 100");
            table.HasCheckConstraint(
                "chk_seo_scores_components",
                "title_score BETWEEN 0 AND 100 AND tags_score BETWEEN 0 AND 100 AND description_score BETWEEN 0 AND 100 AND keyword_optimization_score BETWEEN 0 AND 100 AND tag_relevance_score BETWEEN 0 AND 100");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureModificationTime();

        builder.Property(score => score.OverallSeoScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.TitleScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.TagsScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.DescriptionScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.KeywordOptimizationScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.TagRelevanceScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.KeywordDensity).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.ImprovementSuggestions).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(score => score.ScoringAlgorithmVersion).HasMaxLength(20).HasDefaultValue("v1").IsRequired();

        builder.Property(score => score.CalculatedAtUtc)
            .HasColumnName("calculated_at")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasIndex(score => score.ListingContentId).IsUnique();
        builder.HasIndex(score => score.OverallSeoScore);

        builder.HasQueryFilter(score => score.ListingContent.Product.DeletedAtUtc == null);

        builder.HasOne(score => score.ListingContent)
            .WithOne(content => content.SeoScore)
            .HasForeignKey<SeoScore>(score => score.ListingContentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures listing generation attempts.
/// </summary>
public sealed class ListingGenerationHistoryConfiguration : IEntityTypeConfiguration<ListingGenerationHistory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingGenerationHistory> builder)
    {
        builder.ToTable("listing_generation_history", table =>
        {
            table.HasCheckConstraint(
                "chk_listing_history_user_action",
                "user_action IN ('accepted', 'edited', 'regenerated', 'rejected')");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(history => history.PromptUsed).HasColumnType("text").IsRequired();
        builder.Property(history => history.AdjustmentHint).HasColumnType("text");
        builder.Property(history => history.RawAiResponse).HasColumnType("text").IsRequired();
        builder.Property(history => history.TitleGenerated).HasMaxLength(ColumnLengths.ListingTitle);
        builder.Property(history => history.TagsGenerated).HasColumnType("text[]");
        builder.Property(history => history.DescriptionGenerated).HasColumnType("text");
        builder.Property(history => history.UserAction).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(history => history.FeedbackNotes).HasColumnType("text");

        builder.HasIndex(history => new { history.ListingContentId, history.GenerationNumber })
            .HasDatabaseName("uq_listing_generation_number")
            .IsUnique();

        builder.HasQueryFilter(history => history.ListingContent.Product.DeletedAtUtc == null);

        builder.HasOne(history => history.ListingContent)
            .WithMany(content => content.GenerationHistory)
            .HasForeignKey(history => history.ListingContentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApiUsageRecord>()
            .WithMany()
            .HasForeignKey(history => history.ApiUsageRecordId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
