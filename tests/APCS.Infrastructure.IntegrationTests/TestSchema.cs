using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;

namespace APCS.Infrastructure.IntegrationTests;

public static class TestSchema
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> Ready = [];

    public static async Task EnsureReadyAsync(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        if (Ready.Contains(connectionString))
        {
            return;
        }

        await Gate.WaitAsync();
        try
        {
            if (Ready.Contains(connectionString))
            {
                return;
            }

            var schemaExists = await HasApplicationSchemaAsync(connectionString);
            if (!schemaExists)
            {
                await CreateSchemaAsync(connectionString);
            }

            await ApplyExpressionIndexesAsync(connectionString);

            Ready.Add(connectionString);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<bool> HasApplicationSchemaAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var probe = connection.CreateCommand();
        probe.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'users'";
        return Convert.ToInt64(await probe.ExecuteScalarAsync()) > 0;
    }

    private static async Task CreateSchemaAsync(string connectionString)
    {
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var uuidOssp = connection.CreateCommand();
            uuidOssp.CommandText = "CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\"";
            await uuidOssp.ExecuteNonQueryAsync();
            await using var vector = connection.CreateCommand();
            vector.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
            await vector.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, mapping => mapping.UseVector())
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
    }

    private static async Task ApplyExpressionIndexesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var uniqueName = connection.CreateCommand();
        uniqueName.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ux_style_art_presets_name ON public.style_art_presets (lower(name))";
        await uniqueName.ExecuteNonQueryAsync();
    }
}
