using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.DesignTemplates.Common;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace APCS.Infrastructure.Persistence.Repositories;

/// <summary>Implements ownership-aware design-template persistence with PostgreSQL.</summary>
public sealed class DesignTemplateRepository(AppDbContext dbContext) : IDesignTemplateRepository
{
    private const string PersonalNameConstraint = "ux_design_templates_personal_name_active";

    public async Task<IReadOnlyList<DesignTemplate>> ListSystemAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.DesignTemplates
            .AsNoTracking()
            .Where(template =>
                template.IsSystemTemplate == true &&
                template.IsActive == true &&
                template.DeletedAt == null)
            .OrderByDescending(template => template.CreatedAt)
            .ThenBy(template => template.Name)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<DesignTemplate>> ListPersonalAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.DesignTemplates
            .AsNoTracking()
            .Where(template =>
                template.UserId == userId &&
                template.IsSystemTemplate != true &&
                template.IsActive == true &&
                template.DeletedAt == null)
            .OrderByDescending(template => template.CreatedAt)
            .ThenBy(template => template.Name)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListPersonalNamesAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.DesignTemplates
            .AsNoTracking()
            .Where(template =>
                template.UserId == userId &&
                template.Type == DesignTemplateTypes.Personal &&
                template.IsActive == true &&
                template.DeletedAt == null)
            .Select(template => template.Name)
            .ToArrayAsync(cancellationToken);

    public Task<DesignTemplate?> GetVisibleAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.DesignTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(template =>
                template.Id == id &&
                template.IsActive == true &&
                template.DeletedAt == null &&
                (template.IsSystemTemplate == true || template.UserId == userId),
                cancellationToken);

    public Task<DesignTemplate?> GetForUpdateAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        dbContext.DesignTemplates.SingleOrDefaultAsync(template => template.Id == id, cancellationToken);

    public Task<bool> PersonalNameExistsAsync(
        Guid userId,
        string normalizedName,
        Guid? excludedId = null,
        CancellationToken cancellationToken = default) =>
        dbContext.DesignTemplates
            .AsNoTracking()
            .AnyAsync(template =>
                template.UserId == userId &&
                template.IsSystemTemplate != true &&
                template.DeletedAt == null &&
                (!excludedId.HasValue || template.Id != excludedId.Value) &&
                template.Name.Trim().ToLower() == normalizedName,
                cancellationToken);

    public Task<bool> IsUsedByActiveBatchAsync(
        Guid templateId,
        CancellationToken cancellationToken = default) =>
        dbContext.BatchJobProducts
            .AsNoTracking()
            .AnyAsync(item =>
                item.Product != null &&
                item.Product.DesignTemplateId == templateId &&
                item.BatchJob.DeletedAt == null &&
                ActiveDesignTemplateBatchStatuses.All.Contains(item.BatchJob.Status),
                cancellationToken);

    public async Task<bool> TryAddAsync(
        DesignTemplate template,
        CancellationToken cancellationToken = default)
    {
        dbContext.DesignTemplates.Add(template);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsPersonalNameConflict(exception))
        {
            dbContext.Entry(template).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsPersonalNameConflict(exception))
        {
            return false;
        }
    }

    public async Task IncrementUsageCountAsync(
        Guid templateId,
        CancellationToken cancellationToken = default) =>
        _ = await dbContext.DesignTemplates
            .Where(template => template.Id == templateId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    template => template.UsageCount,
                    template => (template.UsageCount ?? 0) + 1),
                cancellationToken);

    private static bool IsPersonalNameConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: PersonalNameConstraint
        };
}
