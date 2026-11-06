using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans.Common;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.Subscriptions;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>
/// Lets the Admin set what Free and the free trial include: their allowances, and the plan the
/// trial gives. Changes apply at once; a Seller already past a lowered allowance cannot start more
/// until it resets.
/// </summary>
public interface IPlanSettingsAdminService
{
    Task<Result<PlanSettingsDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<Result<PlanSettingsDto>> UpdateAsync(UpdatePlanSettingsRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Marks a plan on sale as Recommended for Sellers, or none.</summary>
    Task<Result<PlanSettingsDto>> SetRecommendedPlanAsync(SetRecommendedPlanRequestDto request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PlanSettingsAdminService(
    IPlanSettings settings,
    IOptions<UsageAllowanceOptions> defaults,
    IRepository<UsageAllowance> allowances,
    IRepository<PlanSetting> planSettings,
    IRepository<SubscriptionPlan> plans,
    IRepository<Subscription> subscriptions,
    IRepository<User> users,
    IRepository<AuditLog> auditLogs,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IPlanSettingsAdminService
{
    private const int MaxLimit = 10_000;

    /// <inheritdoc />
    public async Task<Result<PlanSettingsDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAllowancesAsync(cancellationToken);
        var trialTier = await settings.GetTrialPlanTierAsync(cancellationToken);
        var onSale = await plans.Query().AsNoTracking()
            .Where(plan => plan.IsActive == true)
            .OrderBy(plan => plan.MonthlyPriceUsd)
            .Select(plan => new TrialPlanOptionDto(plan.Tier, plan.Name, plan.MaxBatchSize))
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var activeTrials = await subscriptions.Query().CountAsync(
            subscription => subscription.TrialEndsAt != null && subscription.TrialEndsAt > now
                && subscription.Status == "active" && subscription.DeletedAt == null,
            cancellationToken);

        // The last edit to either table.
        var edits = (await allowances.Query().AsNoTracking()
                .Where(row => row.UpdatedAt != null)
                .Select(row => new { row.UpdatedAt, row.UpdatedBy })
                .ToListAsync(cancellationToken))
            .Concat(await planSettings.Query().AsNoTracking()
                .Where(row => row.UpdatedAt != null)
                .Select(row => new { row.UpdatedAt, row.UpdatedBy })
                .ToListAsync(cancellationToken))
            .OrderByDescending(edit => edit.UpdatedAt)
            .FirstOrDefault();
        var editor = edits?.UpdatedBy is { } editorId
            ? await users.Query().Where(user => user.Id == editorId).Select(user => user.FullName).FirstOrDefaultAsync(cancellationToken)
            : null;

        return Result.Success(new PlanSettingsDto(
            trialTier,
            onSale,
            await settings.GetTrialDaysAsync(cancellationToken),
            AllowanceSettings.All
                .Select(definition => new AllowanceSettingDto(
                    definition.Scope, definition.Code, definition.Label, definition.Get(current), definition.Get(defaults.Value)))
                .ToList(),
            activeTrials,
            edits?.UpdatedAt,
            editor,
            await settings.GetRecommendedPlanTierAsync(cancellationToken)));
    }

    /// <inheritdoc />
    public async Task<Result<PlanSettingsDto>> SetRecommendedPlanAsync(SetRecommendedPlanRequestDto request, CancellationToken cancellationToken = default)
    {
        var tier = string.IsNullOrWhiteSpace(request.Tier) ? null : request.Tier.Trim();
        if (tier is not null && !await plans.Query().AnyAsync(plan => plan.Tier == tier && plan.IsActive == true, cancellationToken))
            return Result.Failure<PlanSettingsDto>(AdminSubscriptionPlanErrors.InvalidSettings("Choose a plan on sale to recommend."));

        var previous = await settings.GetRecommendedPlanTierAsync(cancellationToken);
        if (previous == tier) return await GetAsync(cancellationToken);

        // Stored empty for none, so the default does not come back.
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var row = await planSettings.Query().FirstOrDefaultAsync(item => item.Key == PlanSettingKeys.RecommendedPlanTier, cancellationToken);
        if (row is null)
        {
            await planSettings.AddAsync(
                new PlanSetting { Key = PlanSettingKeys.RecommendedPlanTier, Value = tier ?? string.Empty, UpdatedAt = utcNow, UpdatedBy = currentUser.UserId },
                cancellationToken: cancellationToken);
        }
        else
        {
            row.Value = tier ?? string.Empty;
            row.UpdatedAt = utcNow;
            row.UpdatedBy = currentUser.UserId;
            await planSettings.UpdateAsync(row, cancellationToken: cancellationToken);
        }

        await auditLogs.AddAsync(
            new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = currentUser.UserId,
                ActionType = "SetRecommendedPlan",
                ResourceType = "PlanSettings",
                OldValue = JsonSerializer.Serialize(new { recommendedPlanTier = previous }),
                NewValue = JsonSerializer.Serialize(new { recommendedPlanTier = tier }),
                CreatedAt = utcNow
            },
            cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        settings.Invalidate();

        return await GetAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<PlanSettingsDto>> UpdateAsync(UpdatePlanSettingsRequestDto request, CancellationToken cancellationToken = default)
    {
        var trialPlan = await plans.Query().AsNoTracking()
            .FirstOrDefaultAsync(plan => plan.Tier == request.TrialPlanTier && plan.IsActive == true, cancellationToken);
        if (trialPlan is null)
            return Result.Failure<PlanSettingsDto>(AdminSubscriptionPlanErrors.InvalidSettings("Choose a plan on sale for the free trial."));
        if (request.TrialDays is < 1 or > PlanSettingKeys.MaxTrialDays)
            return Result.Failure<PlanSettingsDto>(AdminSubscriptionPlanErrors.InvalidSettings(
                $"A free trial lasts between 1 and {PlanSettingKeys.MaxTrialDays} days."));

        // The new values over the current ones, so the checks see the whole picture.
        var before = await settings.GetAllowancesAsync(cancellationToken);
        var after = AllowanceSettings.Copy(before);
        foreach (var value in request.Allowances ?? [])
        {
            var definition = AllowanceSettings.All.FirstOrDefault(item => item.Scope == value.Scope && item.Code == value.Code);
            if (definition is null)
                return Result.Failure<PlanSettingsDto>(AdminSubscriptionPlanErrors.InvalidSettings($"Unknown allowance {value.Scope}/{value.Code}."));
            if (value.Limit is < 0 or > MaxLimit)
                return Result.Failure<PlanSettingsDto>(AdminSubscriptionPlanErrors.InvalidSettings($"{definition.Label} must be between 0 and {MaxLimit:N0}."));
            definition.Set(after, value.Limit);
        }

        if (after.Trial.ProductsPerRun < 1)
            return Result.Failure<PlanSettingsDto>(AdminSubscriptionPlanErrors.InvalidSettings("A trial must run at least 1 product at a time."));
        if (after.Trial.ProductsPerRun > after.Trial.Products)
            return Result.Failure<PlanSettingsDto>(AdminSubscriptionPlanErrors.InvalidSettings("Trial products per run cannot be more than the trial's products."));
        if (trialPlan.MaxBatchSize >= 0 && after.Trial.ProductsPerRun > trialPlan.MaxBatchSize)
            return Result.Failure<PlanSettingsDto>(AdminSubscriptionPlanErrors.InvalidSettings(
                $"Trial products per run cannot be more than {trialPlan.Name}'s {trialPlan.MaxBatchSize} per run."));

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var rows = await allowances.Query().ToListAsync(cancellationToken);
        foreach (var definition in AllowanceSettings.All)
        {
            var value = definition.Get(after);
            var row = rows.FirstOrDefault(item => item.Scope == definition.Scope && item.Code == definition.Code);
            if (row is null)
            {
                await allowances.AddAsync(
                    new UsageAllowance { Scope = definition.Scope, Code = definition.Code, LimitValue = value, UpdatedAt = utcNow, UpdatedBy = currentUser.UserId },
                    cancellationToken: cancellationToken);
            }
            else if (row.LimitValue != value)
            {
                row.LimitValue = value;
                row.UpdatedAt = utcNow;
                row.UpdatedBy = currentUser.UserId;
                await allowances.UpdateAsync(row, cancellationToken: cancellationToken);
            }
        }

        var previousTier = await settings.GetTrialPlanTierAsync(cancellationToken);
        var tierRow = await planSettings.Query().FirstOrDefaultAsync(row => row.Key == PlanSettingKeys.TrialPlanTier, cancellationToken);
        if (tierRow is null)
        {
            await planSettings.AddAsync(
                new PlanSetting { Key = PlanSettingKeys.TrialPlanTier, Value = trialPlan.Tier, UpdatedAt = utcNow, UpdatedBy = currentUser.UserId },
                cancellationToken: cancellationToken);
        }
        else if (tierRow.Value != trialPlan.Tier)
        {
            tierRow.Value = trialPlan.Tier;
            tierRow.UpdatedAt = utcNow;
            tierRow.UpdatedBy = currentUser.UserId;
            await planSettings.UpdateAsync(tierRow, cancellationToken: cancellationToken);
        }

        // New trials only: a running trial keeps the end it started with.
        var previousDays = await settings.GetTrialDaysAsync(cancellationToken);
        var trialDays = request.TrialDays ?? previousDays;
        var daysRow = await planSettings.Query().FirstOrDefaultAsync(row => row.Key == PlanSettingKeys.TrialDays, cancellationToken);
        if (daysRow is null)
        {
            if (request.TrialDays is not null)
                await planSettings.AddAsync(
                    new PlanSetting { Key = PlanSettingKeys.TrialDays, Value = trialDays.ToString(), UpdatedAt = utcNow, UpdatedBy = currentUser.UserId },
                    cancellationToken: cancellationToken);
        }
        else if (daysRow.Value != trialDays.ToString())
        {
            daysRow.Value = trialDays.ToString();
            daysRow.UpdatedAt = utcNow;
            daysRow.UpdatedBy = currentUser.UserId;
            await planSettings.UpdateAsync(daysRow, cancellationToken: cancellationToken);
        }

        await auditLogs.AddAsync(
            new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = currentUser.UserId,
                ActionType = "UpdatePlanSettings",
                ResourceType = "PlanSettings",
                OldValue = JsonSerializer.Serialize(new { trialPlanTier = previousTier, trialDays = previousDays, allowances = Snapshot(before) }),
                NewValue = JsonSerializer.Serialize(new { trialPlanTier = trialPlan.Tier, trialDays, allowances = Snapshot(after) }),
                CreatedAt = utcNow
            },
            cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        settings.Invalidate();

        return await GetAsync(cancellationToken);
    }

    private static Dictionary<string, int> Snapshot(UsageAllowanceOptions options) =>
        AllowanceSettings.All.ToDictionary(definition => $"{definition.Scope}.{definition.Code}", definition => definition.Get(options));
}
