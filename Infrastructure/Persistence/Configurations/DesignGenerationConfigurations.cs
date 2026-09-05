using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace APCS.Infrastructure.Persistence.Configurations;

/// <summary>
/// The print-on-demand product types the catalogue supports.
/// </summary>
internal static class ProductTypes
{
    public const string Sql = "('tshirt', 'hoodie', 'mug', 'poster', 'tote_bag', 'phone_case')";
}

/// <summary>
/// The approval states shared by generated assets.
/// </summary>
internal static class ApprovalStatuses
{
    public const string Sql = "('pending', 'approved', 'rejected')";
}

/// <summary>
/// Configures product persistence.
/// </summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", table =>
        {
            table.HasCheckConstraint("chk_products_type", $"product_type IN {ProductTypes.Sql}");
            table.HasCheckConstraint("chk_products_status", $"processing_status IN {PipelineStatuses.Sql}");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(product => product.Name).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(product => product.ProductType).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(product => product.NicheCategory).HasMaxLength(ColumnLengths.Name);
        builder.Property(product => product.TargetAudience).HasMaxLength(ColumnLengths.Name);
        builder.Property(product => product.InputDescription).HasColumnType("text").IsRequired();
        builder.Property(product => product.DesiredDesignText).HasColumnType("text");
        builder.Property(product => product.StylePreset).HasMaxLength(ColumnLengths.LongCode);
        builder.Property(product => product.MainKeywords).HasMaxLength(ColumnLengths.StorageKey);
        builder.Property(product => product.ColorPreference).HasMaxLength(ColumnLengths.Name);
        builder.Property(product => product.Notes).HasColumnType("text");
        builder.Property(product => product.ProcessingStatus)
            .HasMaxLength(ColumnLengths.Code)
            .HasDefaultValue("pending")
            .IsRequired();

        builder.HasIndex(product => product.UserId);
        builder.HasIndex(product => product.DesignTemplateId);
        builder.HasIndex(product => product.ProcessingStatus);
        builder.HasIndex(product => product.CreatedAtUtc);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(product => product.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(product => product.DesignTemplate)
            .WithMany(template => template.Products)
            .HasForeignKey(product => product.DesignTemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures design prompt templates.
/// </summary>
public sealed class DesignTemplateConfiguration : IEntityTypeConfiguration<DesignTemplate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DesignTemplate> builder)
    {
        builder.ToTable("design_templates", table =>
        {
            table.HasCheckConstraint(
                "chk_design_templates_art_style",
                "art_style IN ('vintage', 'minimalist', 'watercolor', 'bold_typography', 'dark_academia', 'funny_quote', 'floral')");

            // A template is system-owned exactly when it has no owning user.
            table.HasCheckConstraint(
                "chk_design_templates_system_owner",
                "(user_id IS NULL) = is_system_template");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();
        builder.ConfigureSoftDelete();

        builder.Property(template => template.Name).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(template => template.Type).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(template => template.NicheCategory).HasMaxLength(ColumnLengths.Name);
        builder.Property(template => template.ArtStyle).HasMaxLength(ColumnLengths.LongCode);
        builder.Property(template => template.BasePrompt).HasColumnType("text").IsRequired();
        builder.Property(template => template.ExamplePrompts).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(template => template.StyleDescription).HasColumnType("text");
        builder.Property(template => template.PreviewImageUrl).HasColumnType("text");
        builder.Property(template => template.IsSystemTemplate).HasDefaultValue(false);
        builder.Property(template => template.IsActive).HasDefaultValue(true);
        builder.Property(template => template.UsageCount).HasDefaultValue(0);

        builder.HasIndex(template => template.UserId);
        builder.HasIndex(template => template.Type);
        builder.HasIndex(template => template.ArtStyle);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(template => template.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Configures mockup backdrop templates.
/// </summary>
public sealed class MockupTemplateConfiguration : IEntityTypeConfiguration<MockupTemplate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MockupTemplate> builder)
    {
        builder.ToTable("mockup_templates", table =>
        {
            table.HasCheckConstraint("chk_mockup_templates_product_type", $"product_type IN {ProductTypes.Sql}");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(template => template.Name).HasMaxLength(ColumnLengths.Name).IsRequired();
        builder.Property(template => template.ProductType).HasMaxLength(ColumnLengths.Code).IsRequired();
        builder.Property(template => template.BaseImageUrl).HasColumnType("text").IsRequired();
        builder.Property(template => template.PreviewImageUrl).HasColumnType("text");
        builder.Property(template => template.PrintAreaConfig).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(template => template.IsSystemTemplate).HasDefaultValue(true);
        builder.Property(template => template.IsActive).HasDefaultValue(true);
        builder.Property(template => template.UsageCount).HasDefaultValue(0);

        builder.HasIndex(template => template.ProductType);
        builder.HasIndex(template => template.IsActive);
    }
}

/// <summary>
/// Configures the mockup templates chosen for a product.
/// </summary>
public sealed class ProductMockupTemplateConfiguration : IEntityTypeConfiguration<ProductMockupTemplate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductMockupTemplate> builder)
    {
        builder.ToTable("product_mockup_templates");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();

        builder.Property(selection => selection.SequenceOrder).HasDefaultValue(1);

        builder.HasIndex(selection => new { selection.ProductId, selection.MockupTemplateId })
            .HasDatabaseName("uq_product_mockup_template")
            .IsUnique();

        builder.HasIndex(selection => selection.MockupTemplateId);

        builder.HasQueryFilter(selection => selection.Product.DeletedAtUtc == null);

        builder.HasOne(selection => selection.Product)
            .WithMany(product => product.MockupTemplates)
            .HasForeignKey(selection => selection.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(selection => selection.MockupTemplate)
            .WithMany()
            .HasForeignKey(selection => selection.MockupTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Configures generated image prompts.
/// </summary>
public sealed class AiPromptConfiguration : IEntityTypeConfiguration<AiPrompt>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AiPrompt> builder)
    {
        builder.ToTable("ai_prompts");

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureModificationTime();

        builder.Property(prompt => prompt.OriginalDescription).HasColumnType("text").IsRequired();
        builder.Property(prompt => prompt.SystemPrompt).HasColumnType("text").IsRequired();
        builder.Property(prompt => prompt.FewShotExamples).HasColumnType("jsonb");
        builder.Property(prompt => prompt.GeneratedPrompt).HasColumnType("text").IsRequired();
        builder.Property(prompt => prompt.VersionNumber).HasDefaultValue(1);
        builder.Property(prompt => prompt.IsApprovedByUser).HasDefaultValue(false);
        builder.Property(prompt => prompt.UserNotes).HasColumnType("text");

        builder.HasIndex(prompt => new { prompt.ProductId, prompt.VersionNumber })
            .HasDatabaseName("uq_ai_prompts_product_version")
            .IsUnique();

        builder.HasIndex(prompt => prompt.ProductId);

        builder.HasQueryFilter(prompt => prompt.Product.DeletedAtUtc == null);

        builder.HasOne(prompt => prompt.Product)
            .WithMany(product => product.Prompts)
            .HasForeignKey(prompt => prompt.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(prompt => prompt.DesignTemplate)
            .WithMany()
            .HasForeignKey(prompt => prompt.DesignTemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures generated design images.
/// </summary>
public sealed class DesignImageConfiguration : IEntityTypeConfiguration<DesignImage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DesignImage> builder)
    {
        builder.ToTable("design_images", table =>
        {
            table.HasCheckConstraint("chk_design_images_approval", $"approval_status IN {ApprovalStatuses.Sql}");
            table.HasCheckConstraint("chk_design_images_rating", "user_rating IS NULL OR user_rating BETWEEN 1 AND 5");
            table.HasCheckConstraint("chk_design_images_quality_score", "quality_score IS NULL OR quality_score BETWEEN 0 AND 1");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(image => image.ImageGeneratorModel).HasMaxLength(ColumnLengths.LongCode).IsRequired();
        builder.Property(image => image.StorageProvider).HasMaxLength(ColumnLengths.Code).HasDefaultValue("s3").IsRequired();
        builder.Property(image => image.StorageKey).HasMaxLength(ColumnLengths.StorageKey).IsRequired();
        builder.Property(image => image.ImageUrl).HasColumnType("text").IsRequired();
        builder.Property(image => image.FileFormat).HasMaxLength(10).IsRequired();
        builder.Property(image => image.FileSizeMb).HasPrecision(10, 2).IsRequired();
        builder.Property(image => image.QualityScore).HasPrecision(3, 2);
        builder.Property(image => image.ApprovalStatus).HasMaxLength(ColumnLengths.Code).HasDefaultValue("pending").IsRequired();
        builder.Property(image => image.IsFinal).HasDefaultValue(false);
        builder.Property(image => image.VariationIndex).HasDefaultValue(1);
        builder.Property(image => image.GenerationTimeSeconds).HasPrecision(10, 3).IsRequired();
        builder.Property(image => image.GenerationMetadata).HasColumnType("jsonb");

        builder.HasIndex(image => image.ProductId);
        builder.HasIndex(image => image.ApprovalStatus);
        builder.HasIndex(image => image.IsFinal);
        builder.HasIndex(image => image.CreatedAtUtc);

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

        builder.HasOne(image => image.ApiUsageRecord)
            .WithMany()
            .HasForeignKey(image => image.ApiUsageRecordId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Configures rendered product mockups.
/// </summary>
public sealed class MockupImageConfiguration : IEntityTypeConfiguration<MockupImage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MockupImage> builder)
    {
        builder.ToTable("mockup_images", table =>
        {
            table.HasCheckConstraint("chk_mockup_images_approval", $"approval_status IN {ApprovalStatuses.Sql}");
        });

        builder.ConfigureGeneratedId();
        builder.ConfigureCreationTime();
        builder.ConfigureSoftDelete();

        builder.Property(image => image.StorageProvider).HasMaxLength(ColumnLengths.Code).HasDefaultValue("s3").IsRequired();
        builder.Property(image => image.StorageKey).HasMaxLength(ColumnLengths.StorageKey).IsRequired();
        builder.Property(image => image.MockupImageUrl).HasColumnType("text").IsRequired();
        builder.Property(image => image.GenerationTimeSeconds).HasPrecision(10, 2);
        builder.Property(image => image.ApprovalStatus).HasMaxLength(ColumnLengths.Code).HasDefaultValue("pending").IsRequired();
        builder.Property(image => image.IsFinal).HasDefaultValue(true);

        builder.HasIndex(image => image.DesignImageId);
        builder.HasIndex(image => image.ProductId);
        builder.HasIndex(image => image.MockupTemplateId);

        builder.HasOne(image => image.DesignImage)
            .WithMany(design => design.MockupImages)
            .HasForeignKey(image => image.DesignImageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(image => image.Product)
            .WithMany(product => product.MockupImages)
            .HasForeignKey(image => image.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(image => image.MockupTemplate)
            .WithMany(template => template.MockupImages)
            .HasForeignKey(image => image.MockupTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(image => image.ApiUsageRecord)
            .WithMany()
            .HasForeignKey(image => image.ApiUsageRecordId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
