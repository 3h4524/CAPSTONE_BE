using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Admin.SubscriptionPlans.Common;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.Subscriptions;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>
/// Implements the Admin Portal's Subscription Plan use cases.
/// </summary>
public sealed class AdminSubscriptionPlanService(
    IPlanRepository plans,
    ISubscriptionRepository subscriptions,
    IRepository<PlanFeature> planFeatures,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IValidator<CreatePlanRequestDto> createValidator,
    IValidator<UpdatePlanRequestDto> updateValidator,
    IValidator<DeletePlanRequestDto> deleteValidator,
    IOptions<PaymentGatewaySettings> gatewaySettings)
    : IAdminSubscriptionPlanService
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AdminPlanDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var allPlans = await plans.GetAllForAdminAsync(cancellationToken);

        var dtos = new List<AdminPlanDto>(allPlans.Count);
        foreach (var plan in allPlans)
        {
            var activeSubscriberCount = await subscriptions.CountActiveByPlanIdAsync(plan.Id, cancellationToken);
            var trialSubscriberCount = await subscriptions.CountActiveTrialsByPlanIdAsync(plan.Id, cancellationToken);
            var hasAnyReference = await subscriptions.HasAnySubscriptionReferenceAsync(plan.Id, cancellationToken);
            dtos.Add(MapToDto(plan, activeSubscriberCount, canDelete: !hasAnyReference) with
            {
                TrialSubscriberCount = trialSubscriberCount
            });
        }

        return Result.Success<IReadOnlyList<AdminPlanDto>>(dtos);
    }

    /// <inheritdoc />
    public async Task<Result<AdminPlanDto>> GetByIdAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await plans.GetByIdForAdminAsync(planId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<AdminPlanDto>(AdminSubscriptionPlanErrors.PlanNotFound());
        }

        var activeSubscriberCount = await subscriptions.CountActiveByPlanIdAsync(planId, cancellationToken);
        var trialSubscriberCount = await subscriptions.CountActiveTrialsByPlanIdAsync(planId, cancellationToken);
        var hasAnyReference = await subscriptions.HasAnySubscriptionReferenceAsync(planId, cancellationToken);
        return Result.Success(MapToDto(plan, activeSubscriberCount, canDelete: !hasAnyReference) with
        {
            TrialSubscriberCount = trialSubscriberCount
        });
    }

    /// <inheritdoc />
    public async Task<Result<AdminPlanDto>> CreateAsync(
        CreatePlanRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<AdminPlanDto>(validation.ToValidationError());
        }

        var tier = request.Tier.Trim().ToLowerInvariant();
        if (await plans.IsNameOrTierTakenAsync(request.Name, tier, excludePlanId: null, cancellationToken))
        {
            return Result.Failure<AdminPlanDto>(AdminSubscriptionPlanErrors.NameOrTierTaken());
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Tier = tier,
            Description = request.Description,
            MonthlyPriceUsd = request.MonthlyPriceUsd,
            AnnualPriceUsd = request.AnnualPriceUsd,
            MaxBatchSize = request.MaxBatchSize,
            MaxConcurrentJobs = request.MaxConcurrentJobs,
            MaxProductsPerMonth = request.MaxProductsPerMonth,
            ImageGenerationQuota = request.ImageGenerationQuota,
            VideoGenerationQuota = request.VideoGenerationQuota,
            ApiCallQuota = request.ApiCallQuota,
            StorageQuotaGb = request.StorageQuotaGb,
            CustomApiKeysAllowed = request.CustomApiKeysAllowed,
            PrioritySupport = request.PrioritySupport,
            IsActive = request.IsActive,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        await plans.AddAsync(plan, cancellationToken: cancellationToken);

        if (request.WhiteLabelExportEnabled)
        {
            await planFeatures.AddAsync(
                new PlanFeature
                {
                    PlanId = plan.Id,
                    FeatureCode = PlanFeatureCodes.WhiteLabelExport,
                    IsEnabled = true,
                    CreatedAt = utcNow
                },
                cancellationToken: cancellationToken);
        }

        await UpsertFeaturesAsync(plan.Id, request.Features, utcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Read back what was stored; the features just written are not on the new plan object.
        var created = await plans.GetByIdForAdminAsync(plan.Id, cancellationToken);
        return Result.Success(created is not null
            ? MapToDto(created, activeSubscriberCount: 0, canDelete: true)
            : MapToDto(plan, activeSubscriberCount: 0, canDelete: true, request.WhiteLabelExportEnabled, RequestedFeatures(request.Features)));
    }

    /// <inheritdoc />
    public async Task<Result<AdminPlanDto>> UpdateAsync(
        Guid planId,
        UpdatePlanRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<AdminPlanDto>(validation.ToValidationError());
        }

        var plan = await plans.GetByIdForAdminAsync(planId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<AdminPlanDto>(AdminSubscriptionPlanErrors.PlanNotFound());
        }

        if (await plans.IsNameOrTierTakenAsync(request.Name, plan.Tier, planId, cancellationToken))
        {
            return Result.Failure<AdminPlanDto>(AdminSubscriptionPlanErrors.NameOrTierTaken());
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        // BR198/BR199: price, limit, and active-status changes never touch existing subscribers —
        // this only rewrites the plan row itself, never any Subscription.
        plan.Name = request.Name;
        plan.Description = request.Description;
        plan.MonthlyPriceUsd = request.MonthlyPriceUsd;
        plan.AnnualPriceUsd = request.AnnualPriceUsd;
        plan.MaxBatchSize = request.MaxBatchSize;
        plan.MaxConcurrentJobs = request.MaxConcurrentJobs;
        plan.MaxProductsPerMonth = request.MaxProductsPerMonth;
        plan.ImageGenerationQuota = request.ImageGenerationQuota;
        plan.VideoGenerationQuota = request.VideoGenerationQuota;
        plan.ApiCallQuota = request.ApiCallQuota;
        plan.StorageQuotaGb = request.StorageQuotaGb;
        plan.CustomApiKeysAllowed = request.CustomApiKeysAllowed;
        plan.PrioritySupport = request.PrioritySupport;
        plan.IsActive = request.IsActive;
        plan.UpdatedAt = utcNow;

        // The plan was read untracked together with its feature rows. Updating it with them would
        // attach those rows, and the upserts below attach their own copies of the same keys, so
        // the plan row is updated on its own.
        plan.PlanFeatures = [];
        await plans.UpdateAsync(plan, cancellationToken: cancellationToken);
        await UpsertWhiteLabelFeatureAsync(planId, request.WhiteLabelExportEnabled, utcNow, cancellationToken);
        await UpsertFeaturesAsync(planId, request.Features, utcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var activeSubscriberCount = await subscriptions.CountActiveByPlanIdAsync(planId, cancellationToken);
        var trialSubscriberCount = await subscriptions.CountActiveTrialsByPlanIdAsync(planId, cancellationToken);
        var hasAnyReferenceAfterUpdate = await subscriptions.HasAnySubscriptionReferenceAsync(planId, cancellationToken);
        var updated = await plans.GetByIdForAdminAsync(planId, cancellationToken);
        var dto = updated is not null
            ? MapToDto(updated, activeSubscriberCount, !hasAnyReferenceAfterUpdate)
            : MapToDto(plan, activeSubscriberCount, !hasAnyReferenceAfterUpdate, request.WhiteLabelExportEnabled, RequestedFeatures(request.Features));
        return Result.Success(dto with { TrialSubscriberCount = trialSubscriberCount });
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(
        Guid planId,
        DeletePlanRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await deleteValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(validation.ToValidationError());
        }

        var plan = await plans.GetByIdForAdminAsync(planId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure(AdminSubscriptionPlanErrors.PlanNotFound());
        }

        // BR200: only ever hard-deletes, and only when nothing ever referenced this plan. Both
        // subscriptions_plan_id_fkey and subscriptions_scheduled_plan_id_fkey are ON DELETE
        // RESTRICT, so a plan with any historical (not just active) subscription would otherwise
        // fail this DELETE with an unhandled FK violation. A plan with history must be
        // deactivated instead, explicitly, via UpdateAsync's "Plan is active" toggle — this
        // action never falls back to that silently.
        var hasAnyReference = await subscriptions.HasAnySubscriptionReferenceAsync(planId, cancellationToken);
        if (hasAnyReference)
        {
            return Result.Failure(AdminSubscriptionPlanErrors.HasSubscriptionHistory());
        }

        await plans.RemoveAsync(plan, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task UpsertWhiteLabelFeatureAsync(
        Guid planId,
        bool enabled,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var feature = await planFeatures.Query()
            .SingleOrDefaultAsync(
                f => f.PlanId == planId && f.FeatureCode == PlanFeatureCodes.WhiteLabelExport,
                cancellationToken);

        if (feature is null)
        {
            if (!enabled)
            {
                return;
            }

            await planFeatures.AddAsync(
                new PlanFeature
                {
                    PlanId = planId,
                    FeatureCode = PlanFeatureCodes.WhiteLabelExport,
                    IsEnabled = true,
                    CreatedAt = utcNow
                },
                cancellationToken: cancellationToken);
            return;
        }

        feature.IsEnabled = enabled;
        await planFeatures.UpdateAsync(feature, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Writes the plan features the Admin sent. A feature left out of the request keeps its row.
    /// A flag never stores a limit.
    /// </summary>
    private async Task UpsertFeaturesAsync(
        Guid planId,
        IReadOnlyList<PlanFeatureInputDto>? features,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (features is null || features.Count == 0)
        {
            return;
        }

        var existing = await planFeatures.Query()
            .Where(feature => feature.PlanId == planId)
            .ToListAsync(cancellationToken);

        foreach (var input in features)
        {
            var isLimit = PlanFeatures.Find(input.Code)?.Kind == PlanFeatureKinds.Limit;
            var limitValue = isLimit ? input.LimitValue : null;
            var row = existing.FirstOrDefault(feature => feature.FeatureCode == input.Code);

            if (row is null)
            {
                await planFeatures.AddAsync(
                    new PlanFeature
                    {
                        PlanId = planId,
                        FeatureCode = input.Code,
                        IsEnabled = input.IsEnabled,
                        LimitValue = limitValue,
                        CreatedAt = utcNow
                    },
                    cancellationToken: cancellationToken);
                continue;
            }

            row.IsEnabled = input.IsEnabled;
            row.LimitValue = limitValue;
            await planFeatures.UpdateAsync(row, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Lists every configurable feature for the plan. A flag with no row is off; a limit with no
    /// row has no limit, which is how <see cref="PlanEntitlements"/> reads them.
    /// </summary>
    private static IReadOnlyList<PlanFeatureInputDto> MapFeatures(IEnumerable<PlanFeature> rows) =>
        PlanFeatures.All
            .Select(definition => rows.FirstOrDefault(feature => feature.FeatureCode == definition.Code) is { } row
                ? new PlanFeatureInputDto(definition.Code, row.IsEnabled ?? false, row.LimitValue)
                : new PlanFeatureInputDto(definition.Code, definition.Kind == PlanFeatureKinds.Limit, null))
            .ToList();

    private static IEnumerable<PlanFeature> RequestedFeatures(IReadOnlyList<PlanFeatureInputDto>? features) =>
        (features ?? []).Select(feature => new PlanFeature
        {
            FeatureCode = feature.Code,
            IsEnabled = feature.IsEnabled,
            LimitValue = feature.LimitValue
        });

    private AdminPlanDto MapToDto(SubscriptionPlan plan, int activeSubscriberCount, bool canDelete) =>
        MapToDto(
            plan,
            activeSubscriberCount,
            canDelete,
            plan.PlanFeatures.Any(f => f.FeatureCode == PlanFeatureCodes.WhiteLabelExport && f.IsEnabled == true));

    private AdminPlanDto MapToDto(
        SubscriptionPlan plan,
        int activeSubscriberCount,
        bool canDelete,
        bool whiteLabelExportEnabled,
        IEnumerable<PlanFeature>? features = null) =>
        new(
            plan.Id,
            plan.Name,
            plan.Tier,
            plan.Description,
            plan.MonthlyPriceUsd,
            plan.AnnualPriceUsd,
            plan.MaxBatchSize,
            plan.MaxConcurrentJobs,
            plan.MaxProductsPerMonth,
            plan.ImageGenerationQuota,
            plan.VideoGenerationQuota,
            plan.ApiCallQuota,
            plan.StorageQuotaGb,
            plan.CustomApiKeysAllowed ?? false,
            whiteLabelExportEnabled,
            plan.PrioritySupport ?? false,
            plan.IsActive ?? false,
            activeSubscriberCount,
            canDelete,
            plan.CreatedAt,
            plan.UpdatedAt,
            MapFeatures(features ?? plan.PlanFeatures))
        {
            // Prices are stored in USD; PayOS charges them at this rate.
            MonthlyPriceVnd = gatewaySettings.Value.ToVnd(plan.MonthlyPriceUsd),
            AnnualPriceVnd = plan.AnnualPriceUsd is { } annual ? gatewaySettings.Value.ToVnd(annual) : null,
            UsdToVndRate = gatewaySettings.Value.UsdToVndRate
        };
}
