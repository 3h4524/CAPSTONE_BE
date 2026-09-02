using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configures product persistence.
/// </summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(product => product.Name).HasMaxLength(150).IsRequired();
        builder.Property(product => product.ProductType).HasMaxLength(32).IsRequired();
        builder.Property(product => product.NicheCategory).HasMaxLength(100);
        builder.Property(product => product.InputDescription).HasColumnType("text").IsRequired();
        builder.Property(product => product.ProcessingStatus).HasMaxLength(24).HasDefaultValue("pending").IsRequired();

        builder.HasIndex(product => product.SellerId);
        builder.HasIndex(product => product.BatchJobId);
        builder.HasIndex(product => product.ProcessingStatus);
        builder.HasIndex(product => product.CreatedAtUtc).IsDescending();

        builder.HasOne<Seller>()
            .WithMany()
            .HasForeignKey(product => product.SellerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(product => product.BatchJob)
            .WithMany()
            .HasForeignKey(product => product.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures design template persistence.
/// </summary>
public sealed class DesignTemplateConfiguration : IEntityTypeConfiguration<DesignTemplate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DesignTemplate> builder)
    {
        builder.ToTable("design_templates", table =>
        {
            table.HasCheckConstraint("ck_design_templates_usage_count", "usage_count >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(template => template.Name).HasMaxLength(150).IsRequired();
        builder.Property(template => template.Type).HasMaxLength(32).IsRequired();
        builder.Property(template => template.NicheCategory).HasMaxLength(100);
        builder.Property(template => template.ArtStyle).HasMaxLength(64);
        builder.Property(template => template.BasePrompt).HasColumnType("text").IsRequired();
        builder.Property(template => template.ExamplePrompts).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(template => template.StyleDescription).HasColumnType("text");
        builder.Property(template => template.PreviewImageUrl).HasColumnType("text");
        builder.Property(template => template.IsActive).HasDefaultValue(true);
        builder.Property(template => template.UsageCount).HasDefaultValue(0);

        builder.HasIndex(template => template.SellerId);
        builder.HasIndex(template => template.Type);
        builder.HasIndex(template => template.ArtStyle);

        builder.HasOne<Seller>()
            .WithMany()
            .HasForeignKey(template => template.SellerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures AI prompt persistence.
/// </summary>
public sealed class AiPromptConfiguration : IEntityTypeConfiguration<AiPrompt>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AiPrompt> builder)
    {
        builder.ToTable("ai_prompts", table =>
        {
            table.HasCheckConstraint("ck_ai_prompts_version", "version_number > 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.HasQueryFilter(prompt => prompt.Product.DeletedAtUtc == null);

        builder.Property(prompt => prompt.UpdatedAtUtc).HasColumnName("modified_at");
        builder.Property(prompt => prompt.OriginalDescription).HasColumnType("text").IsRequired();
        builder.Property(prompt => prompt.SystemPrompt).HasColumnType("text").IsRequired();
        builder.Property(prompt => prompt.FewShotExamples).HasColumnType("jsonb");
        builder.Property(prompt => prompt.GeneratedPrompt).HasColumnType("text").IsRequired();
        builder.Property(prompt => prompt.VersionNumber).HasDefaultValue(1);
        builder.Property(prompt => prompt.IsApprovedByUser).HasDefaultValue(false);
        builder.Property(prompt => prompt.UserNotes).HasColumnType("text");

        builder.HasIndex(prompt => prompt.ProductId);
        builder.HasIndex(prompt => new { prompt.ProductId, prompt.VersionNumber }).IsDescending(false, true);

        builder.HasOne(prompt => prompt.Product)
            .WithMany(product => product.AiPrompts)
            .HasForeignKey(prompt => prompt.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(prompt => prompt.DesignTemplate)
            .WithMany(template => template.AiPrompts)
            .HasForeignKey(prompt => prompt.DesignTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Configures generated design image persistence.
/// </summary>
public sealed class DesignImageConfiguration : IEntityTypeConfiguration<DesignImage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DesignImage> builder)
    {
        builder.ToTable("design_images", table =>
        {
            table.HasCheckConstraint("ck_design_images_dimensions", "image_width_px > 0 AND image_height_px > 0");
            table.HasCheckConstraint("ck_design_images_file_size", "file_size_mb >= 0");
            table.HasCheckConstraint("ck_design_images_quality", "quality_score IS NULL OR quality_score BETWEEN 0 AND 1");
            table.HasCheckConstraint("ck_design_images_rating", "user_rating IS NULL OR user_rating BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_design_images_cost", "generation_time_seconds >= 0 AND api_cost_usd >= 0");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();
        builder.HasQueryFilter(image => image.DeletedAtUtc == null && image.Product.DeletedAtUtc == null);

        builder.Property(image => image.ImageGeneratorModel).HasMaxLength(80).IsRequired();
        builder.Property(image => image.ApiResponseId).HasMaxLength(128);
        builder.Property(image => image.ImageUrl).HasColumnType("text").IsRequired();
        builder.Property(image => image.ImageLocalPath).HasMaxLength(500);
        builder.Property(image => image.FileFormat).HasMaxLength(16).IsRequired();
        builder.Property(image => image.FileSizeMb).HasPrecision(10, 2).IsRequired();
        builder.Property(image => image.QualityScore).HasPrecision(3, 2);
        builder.Property(image => image.ApprovalStatus).HasMaxLength(24).HasDefaultValue("pending").IsRequired();
        builder.Property(image => image.IsFinal).HasDefaultValue(false);
        builder.Property(image => image.VariationIndex).HasDefaultValue(1);
        builder.Property(image => image.GenerationTimeSeconds).HasPrecision(10, 3).IsRequired();
        builder.Property(image => image.ApiCostUsd).HasPrecision(10, 4).IsRequired();
        builder.Property(image => image.GenerationMetadata).HasColumnType("jsonb");

        builder.HasIndex(image => image.ProductId);
        builder.HasIndex(image => image.ApprovalStatus);
        builder.HasIndex(image => image.IsFinal);
        builder.HasIndex(image => image.QualityScore).IsDescending();
        builder.HasIndex(image => image.CreatedAtUtc).IsDescending();

        builder.HasOne(image => image.Product)
            .WithMany(product => product.DesignImages)
            .HasForeignKey(image => image.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(image => image.AiPrompt)
            .WithMany(prompt => prompt.DesignImages)
            .HasForeignKey(image => image.AiPromptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(image => image.BatchJob)
            .WithMany()
            .HasForeignKey(image => image.BatchJobId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures mockup image persistence.
/// </summary>
public sealed class MockupImageConfiguration : IEntityTypeConfiguration<MockupImage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MockupImage> builder)
    {
        builder.ToTable("mockup_images", table =>
        {
            table.HasCheckConstraint("ck_mockup_images_dimensions", "mockup_width_px > 0 AND mockup_height_px > 0");
            table.HasCheckConstraint("ck_mockup_images_cost", "(generation_time_seconds IS NULL OR generation_time_seconds >= 0) AND (api_cost_usd IS NULL OR api_cost_usd >= 0)");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();
        builder.HasQueryFilter(mockup =>
            mockup.DeletedAtUtc == null &&
            mockup.Product.DeletedAtUtc == null &&
            mockup.DesignImage.DeletedAtUtc == null);

        builder.Property(mockup => mockup.MockupTemplateType).HasMaxLength(64).IsRequired();
        builder.Property(mockup => mockup.MockupImageUrl).HasColumnType("text").IsRequired();
        builder.Property(mockup => mockup.MockupLocalPath).HasMaxLength(500);
        builder.Property(mockup => mockup.GenerationTimeSeconds).HasPrecision(10, 2);
        builder.Property(mockup => mockup.ApiCostUsd).HasPrecision(10, 4);
        builder.Property(mockup => mockup.IsFinal).HasDefaultValue(true);

        builder.HasIndex(mockup => mockup.DesignImageId);
        builder.HasIndex(mockup => mockup.ProductId);

        builder.HasOne(mockup => mockup.DesignImage)
            .WithMany(image => image.MockupImages)
            .HasForeignKey(mockup => mockup.DesignImageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(mockup => mockup.Product)
            .WithMany(product => product.MockupImages)
            .HasForeignKey(mockup => mockup.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
