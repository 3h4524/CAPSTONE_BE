using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Admin.SubscriptionPlans.Common;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.Entitlements;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>
/// The Admin's view of the plan feature catalog: which features exist, how they are shown, and
/// which plans include them.
/// </summary>
public interface IFeatureCatalogAdminService
{
    /// <summary>Gets every plan feature, in display order, with the plans that include it.</summary>
    Task<Result<IReadOnlyList<AdminFeatureDto>>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Changes how a plan feature is shown, and records who changed what.</summary>
    Task<Result<AdminFeatureDto>> UpdateAsync(string code, UpdateFeatureRequestDto request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class FeatureCatalogAdminService(
    IFeatureCatalog catalog,
    IRepository<Feature> features,
    IRepository<PlanFeature> planFeatures,
    IRepository<User> users,
    IRepository<AuditLog> auditLogs,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IValidator<UpdateFeatureRequestDto> validator) : IFeatureCatalogAdminService
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AdminFeatureDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entries = await catalog.GetAllAsync(cancellationToken);
        var plansByCode = await PlansByCodeAsync(cancellationToken);
        var names = await EditorNamesAsync(entries.Select(entry => entry.UpdatedBy), cancellationToken);

        return Result.Success<IReadOnlyList<AdminFeatureDto>>(entries
            .Select(entry => ToDto(entry, plansByCode, names))
            .ToList());
    }

    /// <inheritdoc />
    public async Task<Result<AdminFeatureDto>> UpdateAsync(string code, UpdateFeatureRequestDto request, CancellationToken cancellationToken = default)
    {
        var current = await catalog.FindAsync(code, cancellationToken);
        if (current is null)
            return Result.Failure<AdminFeatureDto>(AdminSubscriptionPlanErrors.FeatureNotFound());

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<AdminFeatureDto>(validation.ToValidationError());

        var isLimit = current.Kind == PlanFeatureKinds.Limit;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var row = await features.Query().FirstOrDefaultAsync(feature => feature.Code == code, cancellationToken);
        var isNew = row is null;
        // The catalog fell back to the definition in code; the edit gives the feature its row.
        row ??= new Feature { Code = code, Kind = current.Kind, CreatedAt = utcNow };

        row.Label = request.Label.Trim();
        row.Description = request.Description?.Trim() ?? string.Empty;
        row.GroupName = request.Group.Trim();
        row.Unit = isLimit && !string.IsNullOrWhiteSpace(request.Unit) ? request.Unit.Trim() : null;
        row.DefaultLimit = isLimit ? request.DefaultLimit : null;
        row.TrialAvailable = request.TrialAvailable;
        row.IsActive = request.IsActive;
        row.SortOrder = request.SortOrder;
        row.UpdatedAt = utcNow;
        row.UpdatedBy = currentUser.UserId;

        // Query() reads without tracking, so an existing row is attached for the update.
        if (isNew)
            await features.AddAsync(row, cancellationToken: cancellationToken);
        else
            await features.UpdateAsync(row, cancellationToken: cancellationToken);

        await auditLogs.AddAsync(
            new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = currentUser.UserId,
                ActionType = "UpdatePlanFeature",
                ResourceType = "PlanFeature",
                OldValue = JsonSerializer.Serialize(Snapshot(current.Code, current.Label, current.Description, current.Group, current.Unit, current.DefaultLimit, current.TrialAvailable, current.IsActive, current.SortOrder)),
                NewValue = JsonSerializer.Serialize(Snapshot(row.Code, row.Label, row.Description, row.GroupName, row.Unit, row.DefaultLimit, row.TrialAvailable, row.IsActive, row.SortOrder)),
                CreatedAt = utcNow
            },
            cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        catalog.Invalidate();

        var updated = await catalog.FindAsync(code, cancellationToken);
        var plansByCode = await PlansByCodeAsync(cancellationToken);
        var names = await EditorNamesAsync([updated!.UpdatedBy], cancellationToken);
        return Result.Success(ToDto(updated, plansByCode, names));
    }

    private static object Snapshot(string code, string label, string description, string group, string? unit, int? defaultLimit, bool trialAvailable, bool isActive, int sortOrder) =>
        new { code, label, description, group, unit, defaultLimit, trialAvailable, isActive, sortOrder };

    // A flag is included when switched on; a limit whenever the plan has a row for it.
    private async Task<Dictionary<string, List<string>>> PlansByCodeAsync(CancellationToken cancellationToken)
    {
        var rows = await planFeatures.Query()
            .AsNoTracking()
            .Where(feature => feature.IsEnabled == true || feature.LimitValue != null)
            .OrderBy(feature => feature.Plan.MonthlyPriceUsd)
            .ThenBy(feature => feature.Plan.Name)
            .Select(feature => new { feature.FeatureCode, feature.Plan.Name })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.FeatureCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Name).ToList(), StringComparer.Ordinal);
    }

    private async Task<Dictionary<Guid, string>> EditorNamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        if (wanted.Count == 0) return [];

        return await users.Query()
            .AsNoTracking()
            .Where(user => wanted.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.FullName, cancellationToken);
    }

    private static AdminFeatureDto ToDto(
        FeatureCatalogEntry entry,
        IReadOnlyDictionary<string, List<string>> plansByCode,
        IReadOnlyDictionary<Guid, string> names) =>
        new(
            entry.Code,
            entry.Label,
            entry.Description,
            entry.Group,
            entry.Kind,
            entry.Unit,
            entry.DefaultLimit,
            entry.TrialAvailable,
            entry.IsActive,
            entry.SortOrder,
            plansByCode.TryGetValue(entry.Code, out var plans) ? plans : [],
            entry.UpdatedAt,
            entry.UpdatedBy is { } id && names.TryGetValue(id, out var name) ? name : null);
}
