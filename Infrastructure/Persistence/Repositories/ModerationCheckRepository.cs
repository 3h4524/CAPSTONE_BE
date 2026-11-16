using System.Text.Json;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Persistence.Dtos;
using APCS.Common.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class ModerationCheckRepository(AppDbContext db) : IModerationCheckRepository
{
    private const string UndefinedTable = "42P01";

    public async Task<PagedResult<ModerationCheckRow>> SearchAsync(ModerationCheckSearch search,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, search.Page);
        var pageSize = Math.Clamp(search.PageSize, 1, 100);
        var checks = db.ModerationChecks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search.Decision)) checks = checks.Where(check => check.Decision == search.Decision);
        if (!string.IsNullOrWhiteSpace(search.DecidedBy)) checks = checks.Where(check => check.DecidedBy == search.DecidedBy);
        if (!string.IsNullOrWhiteSpace(search.Feature)) checks = checks.Where(check => check.Feature == search.Feature);
        if (!string.IsNullOrWhiteSpace(search.Direction)) checks = checks.Where(check => check.Direction == search.Direction);
        if (search.FromUtc is { } from) checks = checks.Where(check => check.CreatedAt >= from);
        if (search.ToUtc is { } to) checks = checks.Where(check => check.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(search.Category))
        {
            // categories is a jsonb array of findings: [{"category":"sexual",...}].
            var containing = JsonSerializer.Serialize(new[] { new { category = search.Category } });
            checks = checks.Where(check => EF.Functions.JsonContains(check.Categories, containing));
        }

        var rows =
            from check in checks
            join user in db.Users.AsNoTracking() on check.UserId equals user.Id into owners
            from owner in owners.DefaultIfEmpty()
            select new { check, owner };
        if (!string.IsNullOrWhiteSpace(search.Seller))
        {
            var pattern = $"%{search.Seller.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            rows = rows.Where(row => row.owner != null &&
                                     (EF.Functions.ILike(row.owner.Email, pattern) || EF.Functions.ILike(row.owner.FullName, pattern)));
        }

        try
        {
            var total = await rows.CountAsync(cancellationToken);
            var items = await rows
                .OrderByDescending(row => row.check.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(row => new ModerationCheckRow(
                    row.check.Id,
                    row.check.CreatedAt,
                    row.check.UserId,
                    row.owner != null ? row.owner.Email : null,
                    row.owner != null ? row.owner.FullName : null,
                    row.check.Feature,
                    row.check.Direction,
                    row.check.SubjectType,
                    row.check.Decision,
                    row.check.DecidedBy,
                    row.check.Categories,
                    row.check.ContentExcerpt,
                    row.check.LatencyMs))
                .ToListAsync(cancellationToken);
            return new PagedResult<ModerationCheckRow>(items, total, page, pageSize);
        }
        catch (PostgresException error) when (error.SqlState == UndefinedTable)
        {
            return new PagedResult<ModerationCheckRow>([], 0, page, pageSize);
        }
    }
}
