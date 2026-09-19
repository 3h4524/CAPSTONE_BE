using APCS.Domain.Entities;
using APCS.Infrastructure.Persistence;
using APCS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace APCS.Infrastructure.IntegrationTests;

[TestClass]
public sealed class StyleArtPresetRepositoryTests
{
    [TestMethod]
    public async Task AddAsync_PostgreSql_EnforcesCaseInsensitiveUniqueName()
    {
        var connectionString = Environment.GetEnvironmentVariable("APCS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("Set APCS_TEST_CONNECTION_STRING to run the opt-in PostgreSQL test.");
            return;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new AppDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TEMP TABLE style_art_presets (LIKE public.style_art_presets INCLUDING ALL) ON COMMIT DROP");
            var repository = new Repository<StyleArtPreset>(db);
            var now = DateTime.UtcNow;
            await repository.AddAsync(new StyleArtPreset
            {
                Id = Guid.NewGuid(),
                Name = "Neon",
                Description = "Bold glow",
                StyleModifiers = "neon glow",
                Recommendations = "[]",
                IsSystemTemplate = true,
                IsActive = true,
                UsageCount = 0,
                CreatedAt = now,
                UpdatedAt = now
            }, true);

            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => repository.AddAsync(new StyleArtPreset
            {
                Id = Guid.NewGuid(),
                Name = "NEON",
                Description = "Duplicate",
                StyleModifiers = "neon glow",
                Recommendations = "[]",
                IsSystemTemplate = false,
                IsActive = true,
                UsageCount = 0,
                CreatedAt = now,
                UpdatedAt = now
            }, true));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [TestMethod]
    public async Task Query_PostgreSql_FiltersOwnerAndOrdersByUsageThenName()
    {
        var connectionString = Environment.GetEnvironmentVariable("APCS_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("Set APCS_TEST_CONNECTION_STRING to run the opt-in PostgreSQL test.");
            return;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new AppDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TEMP TABLE style_art_presets (LIKE public.style_art_presets INCLUDING ALL) ON COMMIT DROP");
            var owner = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pg_temp.style_art_presets
                    (id, user_id, name, description, style_modifiers, recommendations, is_system_template, is_active, usage_count, created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, NULL, 'Vintage', 'Retro', 'vintage', '[]', TRUE, TRUE, 9, {now}, {now}),
                    ({Guid.NewGuid()}, {owner}, 'Mine', 'Own', 'own', '[]', FALSE, TRUE, 1, {now}, {now}),
                    ({Guid.NewGuid()}, {Guid.NewGuid()}, 'Other', 'Foreign', 'x', '[]', FALSE, TRUE, 99, {now}, {now}),
                    ({Guid.NewGuid()}, NULL, 'Retired', 'Old', 'old', '[]', TRUE, FALSE, 99, {now}, {now})
                """);

            var repository = new Repository<StyleArtPreset>(db);
            var names = await repository.Query()
                .Where(preset => preset.IsActive && (preset.UserId == null || preset.UserId == owner))
                .OrderByDescending(preset => preset.UsageCount)
                .ThenBy(preset => preset.Name)
                .Select(preset => preset.Name)
                .ToListAsync();

            CollectionAssert.AreEqual(new[] { "Vintage", "Mine" }, names);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
