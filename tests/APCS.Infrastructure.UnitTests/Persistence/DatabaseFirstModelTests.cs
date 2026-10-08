using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace APCS.Infrastructure.UnitTests.Persistence;

[TestClass]
public sealed class DatabaseFirstModelTests
{
    [TestMethod]
    public void Model_ReverseEngineeredPublicSchema_ContainsExactFiftySixMappedTablesIncludingWorkflowRuntime()
    {
        using var context = CreateContext();

        var mappedTables = context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        mappedTables.Should().HaveCount(56);
        mappedTables.Should().BeEquivalentTo(new[]
        {
            "ai_prompts", "api_keys", "api_usage_records", "audit_logs", "auth_tokens", "batches",
            "batch_jobs", "batch_job_logs", "batch_job_products", "design_images", "design_templates",
            "etsy_integrations", "etsy_upload_logs", "export_packages", "export_package_items", "invoices",
            "listing_contents", "listing_descriptions", "listing_generation_history", "listing_tags",
            "listing_tag_items", "listing_titles", "media_jobs", "mockup_images", "mockup_templates",
            "music_tracks", "notification_alerts", "notification_deliveries", "payment_methods", "permissions",
            "plan_features", "printify_integrations", "printify_upload_logs", "products", "product_mockup_templates",
            "promo_videos", "promo_video_scenes", "roles", "role_permissions", "seo_scores", "share_hashtags",
            "social_media_shares", "style_art_presets", "subscriptions", "subscription_plans", "support_tickets",
            "ticket_attachments", "ticket_replies", "usage_statistics", "users", "user_profiles", "user_roles",
            "video_templates", "workflows", "workflow_node_runs", "workflow_runs"
        });
    }

    [TestMethod]
    public void Model_VideoWorkflowSchema_MapsDurableSnapshotsLeaseAndOwnerScopedIdempotency()
    {
        using var context = CreateContext();
        var run = context.Model.FindEntityType(typeof(WorkflowRun))!;
        run.GetTableName().Should().Be("workflow_runs");
        run.FindProperty(nameof(WorkflowRun.DefinitionSnapshot))!.GetColumnType().Should().Be("jsonb");
        run.FindProperty(nameof(WorkflowRun.Revision))!.IsNullable.Should().BeFalse();
        run.GetIndexes().Should().Contain(x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(WorkflowRun.UserId), nameof(WorkflowRun.IdempotencyKey) }));
        var job = context.Model.FindEntityType(typeof(MediaJob))!;
        job.GetTableName().Should().Be("media_jobs");
        job.FindProperty(nameof(MediaJob.Payload))!.GetColumnType().Should().Be("jsonb");
        job.FindProperty(nameof(MediaJob.LeaseToken))!.IsNullable.Should().BeTrue();
        job.FindProperty(nameof(MediaJob.LeaseExpiresAt))!.IsNullable.Should().BeTrue();
        job.FindProperty(nameof(MediaJob.MaximumAttempts))!.IsNullable.Should().BeFalse();
        context.Model.FindEntityType(typeof(WorkflowNodeRun))!.GetTableName().Should().Be("workflow_node_runs");
        context.Model.FindEntityType(typeof(Workflow))!.GetTableName().Should().Be("workflows");
    }

    [TestMethod]
    public void Model_UploadedMockup_DoesNotRequireDesignOrTemplateAndStoresApprovalRevision()
    {
        using var context = CreateContext();
        var mockup = context.Model.FindEntityType(typeof(MockupImage))!;
        mockup.FindProperty(nameof(MockupImage.DesignImageId))!.IsNullable.Should().BeTrue();
        mockup.FindProperty(nameof(MockupImage.MockupTemplateId))!.IsNullable.Should().BeTrue();
        mockup.FindProperty(nameof(MockupImage.MockupImageUrl))!.IsNullable.Should().BeTrue();
        mockup.FindProperty(nameof(MockupImage.MetadataRevision))!.IsNullable.Should().BeFalse();
        mockup.FindProperty(nameof(MockupImage.ApprovedRevision))!.IsNullable.Should().BeTrue();
        mockup.FindProperty(nameof(MockupImage.Regions))!.GetColumnType().Should().Be("jsonb");
    }

    [TestMethod]
    public void BatchPipelineModel_LinksProductsAndStageOutputsToTheirJobItem()
    {
        using var context = CreateContext();

        var product = context.Model.FindEntityType(typeof(Product))!;
        product.FindProperty(nameof(Product.BatchId))!.IsNullable.Should().BeFalse();

        var batchJob = context.Model.FindEntityType(typeof(BatchJob))!;
        batchJob.FindProperty(nameof(BatchJob.BatchId))!.IsNullable.Should().BeFalse();
        batchJob.FindProperty(nameof(BatchJob.JobType))!.IsNullable.Should().BeFalse();

        var jobProduct = context.Model.FindEntityType(typeof(BatchJobProduct))!;
        jobProduct.FindProperty(nameof(BatchJobProduct.BatchId))!.IsNullable.Should().BeFalse();

        foreach (var outputType in new[]
                 {
                     typeof(AiPrompt),
                     typeof(DesignImage),
                     typeof(MockupImage),
                     typeof(PromoVideo),
                     typeof(ListingContent)
                 })
        {
            var output = context.Model.FindEntityType(outputType)!;
            output.GetForeignKeys().Should().Contain(foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(BatchJobProduct)
                && foreignKey.Properties.Single().Name == "BatchJobProductId");
        }
    }

    [TestMethod]
    public void Model_AuthToken_UsesPostgresXminForOptimisticConcurrency()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(AuthToken));
        var xmin = entityType!.FindProperty("xmin");

        entityType.GetTableName().Should().Be("auth_tokens");
        xmin.Should().NotBeNull();
        xmin!.IsConcurrencyToken.Should().BeTrue();
    }

    [TestMethod]
    public void Model_StyleArtPresets_MapsExpectedIndexes()
    {
        using var context = CreateContext();

        var indexNames = context.Model.FindEntityType(typeof(StyleArtPreset))!
            .GetIndexes()
            .Select(index => index.GetDatabaseName())
            .ToArray();

        indexNames.Should().Contain("idx_style_art_presets_is_active");
        indexNames.Should().Contain("idx_style_art_presets_user_id");
    }

    [TestMethod]
    public void DatabaseFirstContext_HasNoCodeFirstMigrations()
    {
        using var context = CreateContext();

        context.Database.GetMigrations().Should().BeEmpty();
    }

    [TestMethod]
    public void Model_DesignTemplate_MapsNullableNegativePromptColumn()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(DesignTemplate));
        var property = entityType!.FindProperty(nameof(DesignTemplate.NegativePrompt));
        var table = StoreObjectIdentifier.Table("design_templates", null);

        entityType.GetTableName().Should().Be("design_templates");
        property.Should().NotBeNull();
        property!.GetColumnName(table).Should().Be("negative_prompt");
        property.IsNullable.Should().BeTrue();
        property.GetColumnType().Should().Be("text");
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_metadata;Username=postgres;Password=not-used")
            .Options;

        return new AppDbContext(options);
    }
}
