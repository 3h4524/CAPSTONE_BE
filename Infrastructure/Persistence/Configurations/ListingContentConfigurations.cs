using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures generated listing content persistence.
/// </summary>
public sealed class ListingContentConfiguration : IEntityTypeConfiguration<ListingContent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingContent> builder)
    {
        builder.ToTable("listing_contents", table =>
        {
            table.HasCheckConstraint("ck_listing_contents_cost", "generation_time_seconds >= 0 AND api_cost_usd >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.HasQueryFilter(content => content.Product.DeletedAtUtc == null);

        builder.Property(content => content.AiModelUsed).HasMaxLength(80).IsRequired();
        builder.Property(content => content.ModelVersion).HasMaxLength(32).IsRequired();
        builder.Property(content => content.GenerationTimeSeconds).HasPrecision(10, 2).IsRequired();
        builder.Property(content => content.ApiCostUsd).HasPrecision(10, 4).IsRequired();
        builder.Property(content => content.ApprovalStatus).HasMaxLength(24).HasDefaultValue("pending").IsRequired();

        builder.HasIndex(content => content.ProductId).IsUnique();
        builder.HasIndex(content => content.ApprovalStatus);

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
/// Configures listing title persistence.
/// </summary>
public sealed class ListingTitleConfiguration : IEntityTypeConfiguration<ListingTitle>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingTitle> builder)
    {
        builder.ToTable("listing_titles", table =>
        {
            table.HasCheckConstraint("ck_listing_titles_character_count", "character_count BETWEEN 0 AND 140");
            table.HasCheckConstraint("ck_listing_titles_seo_score", "seo_score BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_listing_titles_version", "version_number > 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.HasQueryFilter(title => title.ListingContent.Product.DeletedAtUtc == null);

        builder.Property(title => title.UpdatedAtUtc).HasColumnName("modified_at");
        builder.Property(title => title.GeneratedTitle).HasMaxLength(140).IsRequired();
        builder.Property(title => title.SeoScore).HasPrecision(5, 2).HasDefaultValue(0m);
        builder.Property(title => title.UserEdited).HasDefaultValue(false);
        builder.Property(title => title.UserEditedVersion).HasMaxLength(140);
        builder.Property(title => title.VersionNumber).HasDefaultValue(1);

        builder.HasIndex(title => title.ListingContentId);

        builder.HasOne(title => title.ListingContent)
            .WithMany(content => content.Titles)
            .HasForeignKey(title => title.ListingContentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures listing tag persistence.
/// </summary>
public sealed class ListingTagConfiguration : IEntityTypeConfiguration<ListingTag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingTag> builder)
    {
        builder.ToTable("listing_tags", table =>
        {
            table.HasCheckConstraint("ck_listing_tags_count", "tag_count = 13");
            table.HasCheckConstraint("ck_listing_tags_seo_score", "seo_score BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_listing_tags_version", "version_number > 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.HasQueryFilter(tag => tag.ListingContent.Product.DeletedAtUtc == null);

        builder.Property(tag => tag.UpdatedAtUtc).HasColumnName("modified_at");
        builder.Property(tag => tag.GeneratedTags).HasColumnType("character varying(20)[]").IsRequired();
        builder.Property(tag => tag.TagTypes).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(tag => tag.SeoScore).HasPrecision(5, 2).HasDefaultValue(0m);
        builder.Property(tag => tag.UserEdited).HasDefaultValue(false);
        builder.Property(tag => tag.UserEditedVersion).HasColumnType("character varying(20)[]");
        builder.Property(tag => tag.VersionNumber).HasDefaultValue(1);

        builder.HasIndex(tag => tag.ListingContentId);

        builder.HasOne(tag => tag.ListingContent)
            .WithMany(content => content.Tags)
            .HasForeignKey(tag => tag.ListingContentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures listing description persistence.
/// </summary>
public sealed class ListingDescriptionConfiguration : IEntityTypeConfiguration<ListingDescription>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingDescription> builder)
    {
        builder.ToTable("listing_descriptions", table =>
        {
            table.HasCheckConstraint("ck_listing_descriptions_word_count", "word_count BETWEEN 200 AND 500");
            table.HasCheckConstraint("ck_listing_descriptions_seo_score", "seo_score BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_listing_descriptions_version", "version_number > 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.HasQueryFilter(description => description.ListingContent.Product.DeletedAtUtc == null);

        builder.Property(description => description.UpdatedAtUtc).HasColumnName("modified_at");
        builder.Property(description => description.GeneratedDescription).HasColumnType("text").IsRequired();
        builder.Property(description => description.StructureFollowed).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(description => description.SeoScore).HasPrecision(5, 2).HasDefaultValue(0m);
        builder.Property(description => description.UserEdited).HasDefaultValue(false);
        builder.Property(description => description.UserEditedVersion).HasColumnType("text");
        builder.Property(description => description.VersionNumber).HasDefaultValue(1);

        builder.HasIndex(description => description.ListingContentId);

        builder.HasOne(description => description.ListingContent)
            .WithMany(content => content.Descriptions)
            .HasForeignKey(description => description.ListingContentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures SEO score persistence.
/// </summary>
public sealed class SeoScoreConfiguration : IEntityTypeConfiguration<SeoScore>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SeoScore> builder)
    {
        builder.ToTable("seo_scores", table =>
        {
            table.HasCheckConstraint("ck_seo_scores_ranges", "overall_seo_score BETWEEN 0 AND 100 AND title_score BETWEEN 0 AND 100 AND tags_score BETWEEN 0 AND 100 AND description_score BETWEEN 0 AND 100 AND keyword_optimization_score BETWEEN 0 AND 100 AND tag_relevance_score BETWEEN 0 AND 100 AND keyword_density BETWEEN 0 AND 100");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureModificationTime();
        builder.HasQueryFilter(score => score.ListingContent.Product.DeletedAtUtc == null);

        builder.Property(score => score.OverallSeoScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.TitleScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.TagsScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.DescriptionScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.KeywordOptimizationScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.TagRelevanceScore).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.KeywordDensity).HasPrecision(5, 2).IsRequired();
        builder.Property(score => score.ImprovementSuggestions).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(score => score.CalculatedAtUtc).HasColumnName("calculated_at").HasDefaultValueSql("CURRENT_TIMESTAMP").IsRequired();

        builder.HasIndex(score => score.ProductId);
        builder.HasIndex(score => score.OverallSeoScore).IsDescending();

        builder.HasOne(score => score.ListingContent)
            .WithMany(content => content.SeoScores)
            .HasForeignKey(score => score.ListingContentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures listing generation history persistence.
/// </summary>
public sealed class ListingGenerationHistoryConfiguration : IEntityTypeConfiguration<ListingGenerationHistory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingGenerationHistory> builder)
    {
        builder.ToTable("listing_generation_history", table =>
        {
            table.HasCheckConstraint("ck_listing_generation_history_number", "generation_number > 0");
            table.HasCheckConstraint("ck_listing_generation_history_api", "api_response_time_ms >= 0 AND api_tokens_used >= 0 AND api_cost_usd >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.HasQueryFilter(history => history.ListingContent.Product.DeletedAtUtc == null);

        builder.Property(history => history.PromptUsed).HasColumnType("text").IsRequired();
        builder.Property(history => history.RawAiResponse).HasColumnType("text").IsRequired();
        builder.Property(history => history.TitleGenerated).HasMaxLength(140);
        builder.Property(history => history.TagsGenerated).HasColumnType("character varying(20)[]");
        builder.Property(history => history.DescriptionGenerated).HasColumnType("text");
        builder.Property(history => history.ApiCostUsd).HasPrecision(10, 4).IsRequired();
        builder.Property(history => history.UserAction).HasMaxLength(32).IsRequired();
        builder.Property(history => history.FeedbackNotes).HasColumnType("text");

        builder.HasIndex(history => history.ListingContentId);
        builder.HasIndex(history => new { history.ListingContentId, history.GenerationNumber }).IsDescending(false, true);

        builder.HasOne(history => history.ListingContent)
            .WithMany(content => content.GenerationHistory)
            .HasForeignKey(history => history.ListingContentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
