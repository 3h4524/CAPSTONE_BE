using APCS.Application.Abstractions.Persistence;
using APCS.Application.Common.Validation;
using APCS.Application.Features.Admin.SubscriptionPlans.Common;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

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
    IValidator<DeletePlanRequestDto> deleteValidator)
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
            var hasAnyReference = await subscriptions.HasAnySubscriptionReferenceAsync(plan.Id, cancellationToken);
            dtos.Add(MapToDto(plan, activeSubscriberCount, canDelete: !hasAnyReference));
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
        var hasAnyReference = await subscriptions.HasAnySubscriptionReferenceAsync(planId, cancellationToken);
        return Result.Success(MapToDto(plan, activeSubscriberCount, canDelete: !hasAnyReference));
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
            SortOrder = request.SortOrder,
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

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDto(plan, activeSubscriberCount: 0, canDelete: true, request.WhiteLabelExportEnabled));
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
        plan.SortOrder = request.SortOrder;
        plan.UpdatedAt = utcNow;

        await plans.UpdateAsync(plan, cancellationToken: cancellationToken);
        await UpsertWhiteLabelFeatureAsync(planId, request.WhiteLabelExportEnabled, utcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var activeSubscriberCount = await subscriptions.CountActiveByPlanIdAsync(planId, cancellationToken);
        var hasAnyReferenceAfterUpdate = await subscriptions.HasAnySubscriptionReferenceAsync(planId, cancellationToken);
        return Result.Success(MapToDto(plan, activeSubscriberCount, !hasAnyReferenceAfterUpdate, request.WhiteLabelExportEnabled));
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

    private static AdminPlanDto MapToDto(SubscriptionPlan plan, int activeSubscriberCount, bool canDelete) =>
        MapToDto(
            plan,
            activeSubscriberCount,
            canDelete,
            plan.PlanFeatures.Any(f => f.FeatureCode == PlanFeatureCodes.WhiteLabelExport && f.IsEnabled == true));

    private static AdminPlanDto MapToDto(
        SubscriptionPlan plan,
        int activeSubscriberCount,
        bool canDelete,
        bool whiteLabelExportEnabled) =>
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
            plan.SortOrder ?? 0,
            activeSubscriberCount,
            canDelete,
            plan.CreatedAt,
            plan.UpdatedAt);
}
