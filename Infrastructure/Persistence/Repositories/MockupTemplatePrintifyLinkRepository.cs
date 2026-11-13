using APCS.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads and writes <c>mockup_template_printify_links</c> with plain SQL, like <c>media_job_outbox</c>: the table is
/// added by a Flyway migration and is not part of the scaffolded model yet.
/// </summary>
public sealed class MockupTemplatePrintifyLinkRepository(AppDbContext db, ILogger<MockupTemplatePrintifyLinkRepository> logger)
    : IMockupTemplatePrintifyLinkRepository
{
    private const string UndefinedTable = "42P01";

    public async Task<IReadOnlyDictionary<Guid, TemplatePrintifyLink>> GetAsync(IReadOnlyCollection<Guid> templateIds,
        CancellationToken cancellationToken)
    {
        var links = new Dictionary<Guid, TemplatePrintifyLink>();
        if (templateIds.Count == 0) return links;
        try
        {
            var connection = await OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                SELECT mockup_template_id, blueprint_id, blueprint_title, blueprint_image_url, print_provider_id, print_provider_title, default_sizes
                FROM public.mockup_template_printify_links WHERE mockup_template_id = ANY(@ids)
                """, connection, CurrentTransaction());
            command.Parameters.AddWithValue("ids", templateIds.ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var link = new TemplatePrintifyLink(
                    reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetFieldValue<string[]>(6));
                links[link.TemplateId] = link;
            }
        }
        catch (PostgresException exception) when (exception.SqlState == UndefinedTable)
        {
            logger.LogWarning("mockup_template_printify_links does not exist yet; apply its migration. Templates are listed without Printify links.");
        }
        return links;
    }

    public async Task SaveAsync(TemplatePrintifyLink link, Guid updatedBy, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO public.mockup_template_printify_links
                (mockup_template_id, blueprint_id, blueprint_title, blueprint_image_url, print_provider_id, print_provider_title, default_sizes, updated_by)
            VALUES (@template, @blueprint, @title, @image, @provider, @providerTitle, @sizes, @by)
            ON CONFLICT (mockup_template_id) DO UPDATE SET
                blueprint_id = EXCLUDED.blueprint_id, blueprint_title = EXCLUDED.blueprint_title,
                blueprint_image_url = EXCLUDED.blueprint_image_url, print_provider_id = EXCLUDED.print_provider_id,
                print_provider_title = EXCLUDED.print_provider_title, default_sizes = EXCLUDED.default_sizes,
                updated_by = EXCLUDED.updated_by, updated_at = CURRENT_TIMESTAMP
            """, connection, CurrentTransaction());
        command.Parameters.AddWithValue("template", link.TemplateId);
        command.Parameters.AddWithValue("blueprint", link.BlueprintId);
        command.Parameters.AddWithValue("title", link.BlueprintTitle);
        command.Parameters.AddWithValue("image", (object?)link.BlueprintImageUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("provider", (object?)link.PrintProviderId ?? DBNull.Value);
        command.Parameters.AddWithValue("providerTitle", (object?)link.PrintProviderTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("sizes", link.DefaultSizes.ToArray());
        command.Parameters.AddWithValue("by", updatedBy);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "DELETE FROM public.mockup_template_printify_links WHERE mockup_template_id = @template", connection, CurrentTransaction());
        command.Parameters.AddWithValue("template", templateId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    // The context's own connection, so a caller's transaction covers these statements too.
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await db.Database.OpenConnectionAsync(cancellationToken);
        return connection;
    }

    private NpgsqlTransaction? CurrentTransaction() =>
        db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
}
