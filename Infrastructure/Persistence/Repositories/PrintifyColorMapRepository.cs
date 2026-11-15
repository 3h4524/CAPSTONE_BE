using APCS.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>Reads <c>printify_color_map</c> with plain SQL: the table is added by a Flyway migration, not scaffolded.</summary>
public sealed class PrintifyColorMapRepository(AppDbContext db, ILogger<PrintifyColorMapRepository> logger) : IPrintifyColorMapRepository
{
    private const string UndefinedTable = "42P01";

    public async Task<IReadOnlyDictionary<string, string>> NamesAsync(long blueprintId, IReadOnlyCollection<string> hexes,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (hexes.Count == 0) return names;
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await db.Database.OpenConnectionAsync(cancellationToken);
            // The blueprint's own row first, then the shared one.
            await using var command = new NpgsqlCommand("""
                SELECT DISTINCT ON (hex) hex::text, printify_name
                FROM public.printify_color_map
                WHERE hex::text = ANY(@hexes) AND (blueprint_id = @blueprint OR blueprint_id IS NULL)
                ORDER BY hex, blueprint_id NULLS LAST
                """, connection, db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction);
            command.Parameters.AddWithValue("hexes", hexes.Select(hex => hex.Trim().ToUpperInvariant()).Distinct().ToArray());
            command.Parameters.AddWithValue("blueprint", blueprintId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                names[reader.GetString(0).Trim()] = reader.GetString(1).Trim();
        }
        catch (PostgresException exception) when (exception.SqlState == UndefinedTable)
        {
            logger.LogWarning("printify_color_map does not exist yet; apply its migration. Mock-up colors are not mapped to Printify.");
        }
        return names;
    }
}
