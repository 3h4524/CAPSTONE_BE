using APCS.Application.Abstractions.Moderation;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>
/// The moderation tables, each call on a context of its own so a recorded check is never saved with, or rolled back
/// with, what the calling use case has staged. The tables come from a Flyway migration; a database it has not reached
/// yet reads as empty and skips the log, like <see cref="PrintifyColorMapRepository"/>.
/// </summary>
public sealed class ModerationStore(IServiceScopeFactory scopes, ILogger<ModerationStore> logger) : IModerationStore
{
    private const string UndefinedTable = "42P01";

    public async Task<ModerationLists> LoadListsAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            var terms = await db.ModerationTerms.AsNoTracking()
                .Where(term => term.IsActive)
                .Select(term => new ModerationTermEntry(term.Term, term.Category, term.Severity, term.MatchType, term.MatchUnaccented))
                .ToListAsync(cancellationToken);
            var allowlist = await db.ModerationAllowlists.AsNoTracking()
                .Where(phrase => phrase.IsActive)
                .Select(phrase => phrase.Phrase)
                .ToListAsync(cancellationToken);
            return new ModerationLists(terms, allowlist);
        }
        catch (PostgresException error) when (error.SqlState == UndefinedTable)
        {
            logger.LogWarning("The moderation tables do not exist yet; the blocklist is empty until the migration runs");
            return ModerationLists.Empty;
        }
    }

    public async Task<bool> IsStorageReadyAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            await db.ModerationSettings.AsNoTracking().AnyAsync(cancellationToken);
            return true;
        }
        catch (PostgresException error) when (error.SqlState == UndefinedTable)
        {
            return false;
        }
    }

    public async Task<IReadOnlyDictionary<string, string>?> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            return await db.ModerationSettings.AsNoTracking()
                .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);
        }
        catch (PostgresException error) when (error.SqlState == UndefinedTable)
        {
            return null;
        }
    }

    public async Task AddCheckAsync(ModerationCheckRecord record, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ModerationChecks.Add(new ModerationCheck
        {
            UserId = record.UserId,
            Feature = record.Feature,
            Direction = record.Direction,
            SubjectType = record.SubjectType,
            SubjectId = record.SubjectId,
            ContentHash = record.ContentHash,
            Decision = record.Decision,
            DecidedBy = record.DecidedBy,
            Categories = record.CategoriesJson,
            Reason = record.Reason,
            ContentExcerpt = record.ContentExcerpt,
            Model = record.Model,
            LatencyMs = record.LatencyMs,
            PolicyVersion = record.PolicyVersion,
            CreatedAt = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: UndefinedTable })
        {
            logger.LogWarning("The moderation_checks table does not exist yet; the check was not recorded");
        }
    }

    public async Task<string?> GetLatestOutputDecisionAsync(string subjectType, Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            return await db.ModerationChecks.AsNoTracking()
                .Where(check => check.Direction == "output" && check.SubjectType == subjectType && check.SubjectId == subjectId)
                .OrderByDescending(check => check.CreatedAt)
                .Select(check => check.Decision)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (PostgresException error) when (error.SqlState == UndefinedTable)
        {
            return null;
        }
    }
}
