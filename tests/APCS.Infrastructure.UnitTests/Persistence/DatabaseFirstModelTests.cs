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
    public void Model_ReverseEngineeredPublicSchema_ContainsFiftyOneMappedTables()
    {
        using var context = CreateContext();

        var mappedTables = context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        mappedTables.Should().HaveCount(51);
        mappedTables.Should().Contain(["users", "roles", "user_roles", "auth_tokens", "batches"]);
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
        mappedTables.Should().Contain(["users", "roles", "user_roles", "auth_tokens"]);
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
