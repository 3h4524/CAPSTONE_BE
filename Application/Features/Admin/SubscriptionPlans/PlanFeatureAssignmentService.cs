using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans.Common;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Request;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos.Response;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.Subscriptions;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>
/// Puts features on a plan and takes them off, one change at a time.
/// </summary>
/// <remarks>
/// A change that takes something away (removing an on/off feature, lowering a limit) applies to
/// new subscribers at once, while everyone already on the plan keeps the old setting until the
/// period they paid for ends (<see cref="SubscriptionFeatureGrant"/>). A change that gives more
/// applies to everyone at once. A limit is always part of a plan, so it is changed, never removed.
/// </remarks>
public interface IPlanFeatureAssignmentService
{
    /// <summary>Adds features to a plan; a limit gets the value sent.</summary>
    Task<Result<PlanFeatureChangesDto>> AddAsync(Guid planId, AddPlanFeaturesRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Sets a limit feature's value; null means no limit.</summary>
    Task<Result<PlanFeatureChangesDto>> UpdateLimitAsync(Guid planId, string code, int? limitValue, CancellationToken cancellationToken = default);

    /// <summary>Takes an on/off feature off a plan.</summary>
    Task<Result<PlanFeatureChangesDto>> RemoveAsync(Guid planId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows who a change would reach. Without <paramref name="limitValue"/> it previews removing an
    /// on/off feature; with it, setting a limit to that value (-1 for no limit).
    /// </summary>
    Task<Result<PlanFeatureImpactDto>> GetImpactAsync(Guid planId, string code, int? limitValue, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class PlanFeatureAssignmentService(
    IRepository<SubscriptionPlan> plans,
    IRepository<PlanFeature> planFeatures,
    IRepository<Subscription> subscriptions,
    IRepository<SubscriptionFeatureGrant> grants,
    IRepository<AuditLog> auditLogs,
    IFeatureCatalog catalog,
    IPlanInsightsService insights,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IPlanFeatureAssignmentService
{
    /// <summary>How far back the impact preview counts who used a feature.</summary>
    public const int UsageDays = 30;

    /// <summary>How long a subscriber keeps what they were given after their period began.</summary>
    public const int NoticeDays = 30;

    /// <summary>
    /// A change younger than this may be a mistake, so it gives nobody anything to keep: subscribers
    /// keep at most what the plan gave before the changes made in this window.
    /// </summary>
    public static readonly TimeSpan MistakeWindow = TimeSpan.FromHours(24);

    private const int MaxLimit = 10_000;

    private const string ChangeAction = "ChangePlanFeature";

    /// <param name="PaidFor">True when the plan already gave this when the subscriber's period began.</param>
    private sealed record Holder(Guid SubscriptionId, DateOnly KeptUntil, bool PaidFor);

    /// <inheritdoc />
    public async Task<Result<PlanFeatureChangesDto>> AddAsync(Guid planId, AddPlanFeaturesRequestDto request, CancellationToken cancellationToken = default)
    {
        var features = request.Features ?? [];
        if (features.Count == 0 || features.Select(feature => feature.Code).Distinct().Count() != features.Count)
            return Result.Failure<PlanFeatureChangesDto>(AdminSubscriptionPlanErrors.NoFeaturesToAdd());

        var changes = new List<(FeatureCatalogEntry Feature, PlanFeatureValue After)>();
        foreach (var input in features)
        {
            var feature = await catalog.FindAsync(input.Code, cancellationToken);
            if (feature is null) return Result.Failure<PlanFeatureChangesDto>(AdminSubscriptionPlanErrors.FeatureNotFound());

            var isLimit = feature.Kind == PlanFeatureKinds.Limit;
            if (isLimit && !IsValidLimit(input.LimitValue))
                return Result.Failure<PlanFeatureChangesDto>(AdminSubscriptionPlanErrors.LimitOutOfRange());
            changes.Add((feature, new PlanFeatureValue(true, isLimit ? input.LimitValue : null)));
        }

        return await ApplyAsync(planId, changes, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<PlanFeatureChangesDto>> UpdateLimitAsync(Guid planId, string code, int? limitValue, CancellationToken cancellationToken = default)
    {
        var feature = await catalog.FindAsync(code, cancellationToken);
        if (feature is null) return Result.Failure<PlanFeatureChangesDto>(AdminSubscriptionPlanErrors.FeatureNotFound());
        if (feature.Kind != PlanFeatureKinds.Limit || !IsValidLimit(limitValue))
            return Result.Failure<PlanFeatureChangesDto>(AdminSubscriptionPlanErrors.LimitOutOfRange());

        return await ApplyAsync(planId, [(feature, new PlanFeatureValue(true, limitValue))], cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<PlanFeatureChangesDto>> RemoveAsync(Guid planId, string code, CancellationToken cancellationToken = default)
    {
        var feature = await catalog.FindAsync(code, cancellationToken);
        if (feature is null) return Result.Failure<PlanFeatureChangesDto>(AdminSubscriptionPlanErrors.FeatureNotFound());
        if (feature.Kind == PlanFeatureKinds.Limit)
            return Result.Failure<PlanFeatureChangesDto>(AdminSubscriptionPlanErrors.LimitCannotBeRemoved());

        return await ApplyAsync(planId, [(feature, new PlanFeatureValue(false, null))], cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<PlanFeatureImpactDto>> GetImpactAsync(Guid planId, string code, int? limitValue, CancellationToken cancellationToken = default)
    {
        var plan = await plans.Query().FirstOrDefaultAsync(candidate => candidate.Id == planId, cancellationToken);
        if (plan is null) return Result.Failure<PlanFeatureImpactDto>(AdminSubscriptionPlanErrors.PlanNotFound());
        var feature = await catalog.FindAsync(code, cancellationToken);
        if (feature is null) return Result.Failure<PlanFeatureImpactDto>(AdminSubscriptionPlanErrors.FeatureNotFound());

        var isLimit = feature.Kind == PlanFeatureKinds.Limit;
        // -1 asks about "no limit", which a query string cannot send as null.
        var proposedLimit = limitValue is -1 ? null : limitValue;
        if (isLimit && !IsValidLimit(proposedLimit))
            return Result.Failure<PlanFeatureImpactDto>(AdminSubscriptionPlanErrors.LimitOutOfRange());

        var row = await planFeatures.Query().FirstOrDefaultAsync(candidate => candidate.PlanId == planId && candidate.FeatureCode == code, cancellationToken);
        var current = Current(row, feature);
        var proposed = isLimit ? new PlanFeatureValue(true, proposedLimit) : new PlanFeatureValue(false, null);
        var isReduction = EntitlementMerge.Reach(proposed) < EntitlementMerge.Reach(current);

        var onPlan = await OnPlan(planId).ToListAsync(cancellationToken);
        var history = isReduction ? await HistoryAsync(planId, code, current, cancellationToken) : null;
        List<Holder> holders = history is null ? [] : Holders(onPlan, feature, proposed, history);
        var paidFor = holders.Where(holder => holder.PaidFor).ToList();
        var notice = holders.Where(holder => !holder.PaidFor).ToList();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var users = await insights.UsersOfFeatureAsync(code, now.AddDays(-UsageDays), now, cancellationToken);
        int? usedRecently = users is null ? null : onPlan.Count(subscription => users.Contains(subscription.UserId));

        return Result.Success(new PlanFeatureImpactDto(
            feature.Code,
            feature.Label,
            ToDto(current),
            ToDto(proposed),
            onPlan.Count(subscription => subscription.TrialEndsAt == null),
            onPlan.Count(subscription => subscription.TrialEndsAt != null),
            isReduction,
            holders.Count,
            holders.Count > 0 ? holders.Max(holder => holder.KeptUntil) : null,
            usedRecently,
            UsageDays,
            paidFor.Count,
            paidFor.Count > 0 ? paidFor.Max(holder => holder.KeptUntil) : null,
            notice.Count,
            notice.Count > 0 ? notice.Max(holder => holder.KeptUntil) : null,
            history is not null && !TakesAway(proposed, history),
            NoticeDays));
    }

    private async Task<Result<PlanFeatureChangesDto>> ApplyAsync(
        Guid planId,
        IReadOnlyList<(FeatureCatalogEntry Feature, PlanFeatureValue After)> requested,
        CancellationToken cancellationToken)
    {
        var plan = await plans.Query().FirstOrDefaultAsync(candidate => candidate.Id == planId, cancellationToken);
        if (plan is null) return Result.Failure<PlanFeatureChangesDto>(AdminSubscriptionPlanErrors.PlanNotFound());

        var codes = requested.Select(change => change.Feature.Code).ToList();
        var rows = await planFeatures.Query()
            .Where(row => row.PlanId == planId && codes.Contains(row.FeatureCode))
            .ToListAsync(cancellationToken);
        var existingGrants = await grants.Query()
            .Where(grant => grant.PlanId == planId && codes.Contains(grant.FeatureCode))
            .ToListAsync(cancellationToken);
        var onPlan = await OnPlan(planId).ToListAsync(cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var changes = new List<PlanFeatureChangeDto>();
        foreach (var (feature, after) in requested)
        {
            var row = rows.FirstOrDefault(candidate => candidate.FeatureCode == feature.Code);
            var before = Current(row, feature);
            var featureGrants = existingGrants.Where(grant => grant.FeatureCode == feature.Code).ToList();

            // Taking something away: who paid for it keeps it to the end of their period, who was
            // given it since keeps it for the notice days, and undoing a recent mistake leaves nothing.
            var history = EntitlementMerge.Reach(after) < EntitlementMerge.Reach(before)
                ? await HistoryAsync(planId, feature.Code, before, cancellationToken)
                : null;
            List<Holder> holders = history is null ? [] : Holders(onPlan, feature, after, history);
            foreach (var holder in holders)
                await KeepAsync(holder, planId, feature, history!.Settled, featureGrants, utcNow, cancellationToken);

            // A grant the plan now matches or beats no longer gives anything.
            foreach (var grant in featureGrants.Where(grant => EntitlementMerge.Reach(new PlanFeatureValue(grant.IsEnabled, grant.LimitValue)) <= EntitlementMerge.Reach(after)).ToList())
            {
                await grants.RemoveAsync(grant, cancellationToken: cancellationToken);
                featureGrants.Remove(grant);
            }

            await WriteRowAsync(row, planId, feature.Code, after, utcNow, cancellationToken);
            await auditLogs.AddAsync(
                new AuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = currentUser.UserId,
                    ActionType = ChangeAction,
                    ResourceType = "SubscriptionPlan",
                    ResourceId = planId,
                    OldValue = JsonSerializer.Serialize(new { code = feature.Code, isEnabled = before.IsEnabled, limitValue = before.LimitValue }),
                    NewValue = JsonSerializer.Serialize(new
                    {
                        code = feature.Code,
                        isEnabled = after.IsEnabled,
                        limitValue = after.LimitValue,
                        keptBy = holders.Count,
                        keptUntil = holders.Count > 0 ? holders.Max(holder => holder.KeptUntil) : (DateOnly?)null,
                    }),
                    CreatedAt = utcNow
                },
                cancellationToken: cancellationToken);

            changes.Add(new PlanFeatureChangeDto(
                feature.Code,
                feature.Label,
                feature.Kind,
                ToDto(before),
                ToDto(after),
                holders.Count,
                holders.Count > 0 ? holders.Max(holder => holder.KeptUntil) : null));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new PlanFeatureChangesDto(plan.Id, plan.Name, changes));
    }

    // Subscriptions in effect on the plan, trials included.
    private IQueryable<Subscription> OnPlan(Guid planId) =>
        subscriptions.Query().Where(subscription => subscription.PlanId == planId
            && (subscription.Status == "active" || subscription.Status == "past_due")
            && subscription.DeletedAt == null);

    // Who keeps the old setting, and until when. A subscriber whose period began with the plan
    // already giving it paid for it and keeps it to the end of the period: a paid period ends on its
    // renewal date, a trial the day after it ends. One given it after their period began keeps it
    // for the notice days, never past the period. A change that only undoes what the mistake window
    // gave leaves nothing kept. A trial never had what trials leave out, and a period already over
    // has nothing left to keep.
    private List<Holder> Holders(IEnumerable<Subscription> onPlan, FeatureCatalogEntry feature, PlanFeatureValue after, FeatureHistory history)
    {
        if (!TakesAway(after, history)) return [];

        var givenAt = history.GivenAt;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var noticeEnds = today.AddDays(NoticeDays);
        return onPlan
            .Where(subscription => subscription.TrialEndsAt == null || feature.TrialAvailable)
            .Select(subscription =>
            {
                var (periodStart, periodEnd) = Period(subscription);
                var paidFor = givenAt is null || DateOnly.FromDateTime(givenAt.Value) <= periodStart;
                return new Holder(subscription.Id, paidFor || periodEnd < noticeEnds ? periodEnd : noticeEnds, paidFor);
            })
            .Where(holder => holder.KeptUntil > today)
            .ToList();
    }

    // The period a subscription is in: a paid one runs one billing cycle up to its renewal date, a
    // trial its whole length, from the day it started up to the day after it ends.
    private static (DateOnly Start, DateOnly End) Period(Subscription subscription) =>
        subscription.TrialEndsAt is { } trialEnds
            ? (subscription.StartDate, DateOnly.FromDateTime(trialEnds).AddDays(1))
            : (subscription.BillingCycle == "annual" ? subscription.RenewalDate.AddYears(-1) : subscription.RenewalDate.AddMonths(-1),
                subscription.RenewalDate);

    private static bool TakesAway(PlanFeatureValue after, FeatureHistory history) =>
        EntitlementMerge.Reach(after) < EntitlementMerge.Reach(history.Settled);

    // Settled is what the plan gave before any change younger than the mistake window. GivenAt is when
    // it last started giving more before that window; null when it has given it since before changes
    // were logged.
    private sealed record FeatureHistory(PlanFeatureValue Settled, DateTime? GivenAt);

    // The change log, newest first, so each change in the window moves Settled further back.
    private async Task<FeatureHistory> HistoryAsync(Guid planId, string code, PlanFeatureValue current, CancellationToken cancellationToken)
    {
        var logged = await auditLogs.Query()
            .Where(log => log.ActionType == ChangeAction && log.ResourceId == planId)
            .OrderByDescending(log => log.CreatedAt)
            .Select(log => new { log.OldValue, log.NewValue, log.CreatedAt })
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var settled = current;
        foreach (var log in logged)
        {
            var before = ReadSetting(log.OldValue);
            var after = ReadSetting(log.NewValue);
            if (after?.Code != code || before is null) continue;
            if (log.CreatedAt is { } at && now - at < MistakeWindow)
            {
                settled = before.Value;
                continue;
            }

            if (EntitlementMerge.Reach(after.Value) > EntitlementMerge.Reach(before.Value))
                return new FeatureHistory(settled, log.CreatedAt);
        }

        return new FeatureHistory(settled, null);
    }

    private sealed record LoggedSetting(string Code, PlanFeatureValue Value);

    private static LoggedSetting? ReadSetting(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("code", out var code) || !root.TryGetProperty("isEnabled", out var isEnabled)) return null;
            int? limit = root.TryGetProperty("limitValue", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
            return new LoggedSetting(code.GetString() ?? "", new PlanFeatureValue(isEnabled.GetBoolean(), limit));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task KeepAsync(
        Holder holder,
        Guid planId,
        FeatureCatalogEntry feature,
        PlanFeatureValue before,
        List<SubscriptionFeatureGrant> featureGrants,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var reason = feature.Kind == PlanFeatureKinds.Limit && before.IsEnabled ? EntitlementMerge.LimitLowered : EntitlementMerge.FeatureRemoved;
        var grant = featureGrants.FirstOrDefault(candidate => candidate.SubscriptionId == holder.SubscriptionId);
        if (grant is null)
        {
            grant = new SubscriptionFeatureGrant
            {
                Id = Guid.NewGuid(),
                SubscriptionId = holder.SubscriptionId,
                PlanId = planId,
                FeatureCode = feature.Code,
                IsEnabled = before.IsEnabled,
                LimitValue = before.LimitValue,
                ExpiresOn = holder.KeptUntil,
                Reason = reason,
                CreatedBy = currentUser.UserId,
                CreatedAt = utcNow
            };
            featureGrants.Add(grant);
            await grants.AddAsync(grant, cancellationToken: cancellationToken);
            return;
        }

        // Already keeping something from an earlier change: keep whichever goes further.
        if (EntitlementMerge.Reach(before) > EntitlementMerge.Reach(new PlanFeatureValue(grant.IsEnabled, grant.LimitValue)))
        {
            grant.IsEnabled = before.IsEnabled;
            grant.LimitValue = before.LimitValue;
            grant.Reason = reason;
        }
        if (holder.KeptUntil > grant.ExpiresOn) grant.ExpiresOn = holder.KeptUntil;
        await grants.UpdateAsync(grant, cancellationToken: cancellationToken);
    }

    private async Task WriteRowAsync(PlanFeature? row, Guid planId, string code, PlanFeatureValue after, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (row is null)
        {
            await planFeatures.AddAsync(
                new PlanFeature { PlanId = planId, FeatureCode = code, IsEnabled = after.IsEnabled, LimitValue = after.LimitValue, CreatedAt = utcNow },
                cancellationToken: cancellationToken);
            return;
        }

        row.IsEnabled = after.IsEnabled;
        row.LimitValue = after.LimitValue;
        await planFeatures.UpdateAsync(row, cancellationToken: cancellationToken);
    }

    // What the plan gives now. No row: an on/off feature is off, a limit has no limit.
    private static PlanFeatureValue Current(PlanFeature? row, FeatureCatalogEntry feature) =>
        feature.Kind == PlanFeatureKinds.Limit
            ? new PlanFeatureValue(row?.IsEnabled ?? true, row?.LimitValue)
            : new PlanFeatureValue(row?.IsEnabled ?? false, null);

    private static bool IsValidLimit(int? limit) => limit is null or (>= 0 and <= MaxLimit);

    private static PlanFeatureSettingDto ToDto(PlanFeatureValue value) => new(value.IsEnabled, value.LimitValue);
}
