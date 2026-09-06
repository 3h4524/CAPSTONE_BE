using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.UnitTests.Persistence;

[TestClass]
public sealed class DatabaseFirstModelTests
{
    [TestMethod]
    public void Model_ReverseEngineeredPublicSchema_ContainsFiftyMappedTables()
    {
        using var context = CreateContext();

        var mappedTables = context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        mappedTables.Should().HaveCount(50);
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
    public void DatabaseFirstContext_HasNoCodeFirstMigrations()
    {
        using var context = CreateContext();

        context.Database.GetMigrations().Should().BeEmpty();
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_metadata;Username=postgres;Password=not-used")
            .Options;

        return new AppDbContext(options);
    }
}
