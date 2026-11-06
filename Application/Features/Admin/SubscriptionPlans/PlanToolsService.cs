using System.Text.Json;
using System.Text.RegularExpressions;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Admin.SubscriptionPlans.Common;
using APCS.Application.Features.Admin.SubscriptionPlans.Dtos;
using APCS.Application.Features.Entitlements;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Admin.SubscriptionPlans;

/// <summary>
/// Admin conveniences around the plans: start a plan from a copy of another, put the plans in
/// order, and see who changed what.
/// </summary>
public interface IPlanToolsService
{
    /// <summary>Copies a plan, its prices, limits and features, under a new name and tier. The copy is not on sale.</summary>
    Task<Result<DuplicatedPlanDto>> DuplicateAsync(Guid planId, DuplicatePlanRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Puts the plans in the order given, for the Admin and for Sellers comparing plans.</summary>
    Task<Result> ReorderAsync(ReorderPlansRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>One page of the changes to the plans, their features, the catalog, Free and the trial, newest first.</summary>
    Task<Result<PagedResult<PlanHistoryEntryDto>>> GetHistoryAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed partial class PlanToolsService(
    IPlanRepository planRepository,
    IRepository<SubscriptionPlan> plans,
    IRepository<PlanFeature> planFeatures,
    IRepository<AuditLog> auditLogs,
    IRepository<User> users,
    IFeatureCatalog catalog,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IPlanToolsService
{
    private const string Duplicate = "DuplicatePlan";
    private const string Reorder = "ReorderPlans";
    private const string ChangeFeature = "ChangePlanFeature";
    private const string EditCatalog = "UpdatePlanFeature";
    private const string EditSettings = "UpdatePlanSettings";
    private const string SetRecommended = "SetRecommendedPlan";

    private static readonly string[] Logged =
        [Duplicate, Reorder, ChangeFeature, EditCatalog, EditSettings, SetRecommended, PlanChangeActions.Create, PlanChangeActions.Update, PlanChangeActions.Delete];

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex TierSlug();

    /// <inheritdoc />
    public async Task<Result<DuplicatedPlanDto>> DuplicateAsync(Guid planId, DuplicatePlanRequestDto request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? "";
        var tier = request.Tier?.Trim() ?? "";
        if (name.Length is 0 or > 100)
            return Result.Failure<DuplicatedPlanDto>(AdminSubscriptionPlanErrors.InvalidSettings("Enter a plan name of up to 100 characters."));
        if (!TierSlug().IsMatch(tier))
            return Result.Failure<DuplicatedPlanDto>(AdminSubscriptionPlanErrors.InvalidSettings("The tier slug must be lowercase letters, digits and dashes."));

        var source = await plans.Query().AsNoTracking().FirstOrDefaultAsync(plan => plan.Id == planId, cancellationToken);
        if (source is null) return Result.Failure<DuplicatedPlanDto>(AdminSubscriptionPlanErrors.PlanNotFound());
        if (await planRepository.IsNameOrTierTakenAsync(name, tier, excludePlanId: null, cancellationToken))
            return Result.Failure<DuplicatedPlanDto>(AdminSubscriptionPlanErrors.NameOrTierTaken());

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var lastOrder = await plans.Query().MaxAsync(plan => (int?)plan.SortOrder, cancellationToken) ?? 0;
        var copy = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = name,
            Tier = tier,
            Description = source.Description,
            MonthlyPriceUsd = source.MonthlyPriceUsd,
            AnnualPriceUsd = source.AnnualPriceUsd,
            MaxBatchSize = source.MaxBatchSize,
            MaxProductsPerMonth = source.MaxProductsPerMonth,
            MaxConcurrentJobs = source.MaxConcurrentJobs,
            ImageGenerationQuota = source.ImageGenerationQuota,
            VideoGenerationQuota = source.VideoGenerationQuota,
            ApiCallQuota = source.ApiCallQuota,
            StorageQuotaGb = source.StorageQuotaGb,
            PrioritySupport = source.PrioritySupport,
            CustomApiKeysAllowed = source.CustomApiKeysAllowed,
            // Not on sale until the Admin has checked it.
            IsActive = false,
            SortOrder = Math.Max(lastOrder, 0) + 1,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
        await plans.AddAsync(copy, cancellationToken: cancellationToken);

        var features = await planFeatures.Query().AsNoTracking().Where(feature => feature.PlanId == planId).ToListAsync(cancellationToken);
        foreach (var feature in features)
        {
            await planFeatures.AddAsync(
                new PlanFeature { PlanId = copy.Id, FeatureCode = feature.FeatureCode, IsEnabled = feature.IsEnabled, LimitValue = feature.LimitValue, CreatedAt = utcNow },
                cancellationToken: cancellationToken);
        }

        await auditLogs.AddAsync(
            new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = currentUser.UserId,
                ActionType = Duplicate,
                ResourceType = "SubscriptionPlan",
                ResourceId = copy.Id,
                OldValue = JsonSerializer.Serialize(new { planId = source.Id, name = source.Name }),
                NewValue = JsonSerializer.Serialize(new { planId = copy.Id, name = copy.Name, tier = copy.Tier }),
                CreatedAt = utcNow
            },
            cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new DuplicatedPlanDto(copy.Id, copy.Name, copy.Tier));
    }

    /// <inheritdoc />
    public async Task<Result> ReorderAsync(ReorderPlansRequestDto request, CancellationToken cancellationToken = default)
    {
        var all = await plans.Query().AsNoTracking().ToListAsync(cancellationToken);
        var ids = request.PlanIds ?? [];
        if (ids.Count != all.Count || ids.Distinct().Count() != ids.Count || ids.Any(id => all.All(plan => plan.Id != id)))
            return Result.Failure(AdminSubscriptionPlanErrors.InvalidSettings("Send every plan exactly once."));

        var before = all.OrderBy(plan => plan.SortOrder > 0 ? plan.SortOrder : int.MaxValue).ThenBy(plan => plan.MonthlyPriceUsd).Select(plan => plan.Name).ToList();
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        for (var index = 0; index < ids.Count; index++)
        {
            var plan = all.Single(candidate => candidate.Id == ids[index]);
            if (plan.SortOrder == index + 1) continue;
            plan.SortOrder = index + 1;
            plan.UpdatedAt = utcNow;
            await plans.UpdateAsync(plan, cancellationToken: cancellationToken);
        }

        await auditLogs.AddAsync(
            new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = currentUser.UserId,
                ActionType = Reorder,
                ResourceType = "SubscriptionPlan",
                OldValue = JsonSerializer.Serialize(new { order = before }),
                NewValue = JsonSerializer.Serialize(new { order = ids.Select(id => all.Single(plan => plan.Id == id).Name) }),
                CreatedAt = utcNow
            },
            cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<PlanHistoryEntryDto>>> GetHistoryAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var logged = auditLogs.Query().AsNoTracking().Where(log => Logged.Contains(log.ActionType));
        var total = await logged.CountAsync(cancellationToken);
        // A page past the end shows the last one, so deleting history never leaves an empty page.
        pageNumber = Math.Clamp(pageNumber, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var logs = await logged
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var actorIds = logs.Select(log => log.ActorUserId).OfType<Guid>().Distinct().ToList();
        var actors = await users.Query().AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.FullName, cancellationToken);
        var allPlans = await plans.Query().AsNoTracking().Select(plan => new { plan.Id, plan.Tier, plan.Name }).ToListAsync(cancellationToken);
        var planNames = allPlans.ToDictionary(plan => plan.Id, plan => plan.Name);
        var tierNames = allPlans.ToDictionary(plan => plan.Tier, plan => plan.Name);
        var labels = (await catalog.GetAllAsync(cancellationToken)).ToDictionary(entry => entry.Code, entry => entry.Label);

        var entries = logs
            .Select(log =>
            {
                var planName = log.ResourceId is { } id && planNames.TryGetValue(id, out var found) ? found : null;
                var (kind, summary) = Describe(log, planName, labels, tierNames);
                return new PlanHistoryEntryDto(
                    log.Id,
                    log.CreatedAt ?? DateTime.MinValue,
                    log.ActorUserId is { } actor && actors.TryGetValue(actor, out var actorName) ? actorName : null,
                    kind,
                    log.ActionType is ChangeFeature or Duplicate or PlanChangeActions.Create or PlanChangeActions.Update ? planName : null,
                    summary);
            })
            .ToList();
        return Result.Success(new PagedResult<PlanHistoryEntryDto>(entries, total, pageNumber, pageSize));
    }

    // One sentence for a logged change, in the words the History tab shows.
    private static (string Kind, string Summary) Describe(
        AuditLog log, string? planName, IReadOnlyDictionary<string, string> labels, IReadOnlyDictionary<string, string> tierNames)
    {
        using var oldValue = Parse(log.OldValue);
        using var newValue = Parse(log.NewValue);
        var before = oldValue?.RootElement;
        var after = newValue?.RootElement;
        var plan = planName ?? "a deleted plan";

        switch (log.ActionType)
        {
            case ChangeFeature:
            {
                var code = Text(after, "code") ?? "";
                var label = labels.TryGetValue(code, out var name) ? name : code;
                var wasOn = Bool(before, "isEnabled");
                var isOn = Bool(after, "isEnabled");
                var keptBy = Int(after, "keptBy") ?? 0;
                var keptUntil = Text(after, "keptUntil");
                var kept = keptBy > 0 ? $" {keptBy:N0} subscriber{(keptBy == 1 ? "" : "s")} keep the old setting until {FormatDate(keptUntil)} at the latest." : "";
                if (PlanFeatures.Find(code)?.Kind == PlanFeatureKinds.Limit)
                    return ("limit", $"Set {label} on {plan} from {Limit(before)} to {Limit(after)}.{kept}");
                return wasOn == isOn
                    ? ("feature", $"Kept {label} on {plan} as it was.")
                    : isOn ? ("feature", $"Added {label} to {plan}.") : ("feature", $"Removed {label} from {plan}.{kept}");
            }
            case EditCatalog:
            {
                var oldLabel = Text(before, "label");
                var newLabel = Text(after, "label");
                var changes = new List<string>();
                if (oldLabel != newLabel) changes.Add($"renamed it to {newLabel}");
                if (Bool(before, "isActive") != Bool(after, "isActive"))
                    changes.Add(Bool(after, "isActive") ? "offered it again when adding features" : "hid it from the add list");
                if (Bool(before, "trialAvailable") != Bool(after, "trialAvailable"))
                    changes.Add(Bool(after, "trialAvailable") ? "put it in the free trial" : "took it out of the free trial");
                if (Text(before, "group") != Text(after, "group")) changes.Add($"moved it to {Text(after, "group")}");
                var detail = changes.Count > 0 ? ": " + string.Join(", ", changes) : " (description or order)";
                return ("catalog", $"Edited the feature {oldLabel}{detail}.");
            }
            case EditSettings:
            {
                var changes = new List<string>();
                if (Text(before, "trialPlanTier") != Text(after, "trialPlanTier"))
                    changes.Add($"the trial now gives {(Text(after, "trialPlanTier") is { } tier && tierNames.TryGetValue(tier, out var tierName) ? tierName : Text(after, "trialPlanTier"))}");
                if (Int(before, "trialDays") is { } oldDays && Int(after, "trialDays") is { } newDays && oldDays != newDays)
                    changes.Add($"the trial now lasts {newDays} days instead of {oldDays}");
                if (before?.TryGetProperty("allowances", out var oldAllowances) == true
                    && after?.TryGetProperty("allowances", out var newAllowances) == true)
                {
                    foreach (var item in newAllowances.EnumerateObject())
                    {
                        var previous = oldAllowances.TryGetProperty(item.Name, out var old) ? old.GetInt32() : (int?)null;
                        if (previous != item.Value.GetInt32())
                            changes.Add($"{item.Name.Replace('.', ' ').Replace('_', ' ')} {previous} → {item.Value.GetInt32()}");
                    }
                }
                return ("settings", changes.Count > 0 ? $"Changed Free and trial: {string.Join(", ", changes)}." : "Saved Free and trial with no change.");
            }
            case SetRecommended:
            {
                var recommended = Text(after, "recommendedPlanTier");
                if (recommended is null) return ("settings", "Stopped recommending a plan.");
                return ("settings", $"Set {tierNames.GetValueOrDefault(recommended, recommended)} as the recommended plan.");
            }
            case Duplicate:
                return ("duplicate", $"Duplicated {Text(before, "name")} as {Text(after, "name")} (not on sale).");
            case PlanChangeActions.Create:
                return ("plan", $"Created {Text(after, "name")} at {Usd(after, "monthlyPriceUsd")} a month{(Bool(after, "isActive") ? ", on sale" : ", not on sale")}.");
            case PlanChangeActions.Delete:
                return ("plan", $"Deleted {Text(before, "name")}{(Text(after, "reason") is { Length: > 0 } reason ? $": {reason}" : "")}.");
            case PlanChangeActions.Update:
            {
                var changes = new List<string>();
                if (Text(before, "name") != Text(after, "name")) changes.Add($"renamed to {Text(after, "name")}");
                if (Usd(before, "monthlyPriceUsd") != Usd(after, "monthlyPriceUsd"))
                    changes.Add($"monthly price {Usd(before, "monthlyPriceUsd")} → {Usd(after, "monthlyPriceUsd")}");
                if (Usd(before, "annualPriceUsd") != Usd(after, "annualPriceUsd"))
                    changes.Add($"annual price {Usd(before, "annualPriceUsd")} → {Usd(after, "annualPriceUsd")}");
                if (Int(before, "maxBatchSize") != Int(after, "maxBatchSize"))
                    changes.Add($"products per run {Int(before, "maxBatchSize")} → {Int(after, "maxBatchSize")}");
                if (Int(before, "maxConcurrentJobs") != Int(after, "maxConcurrentJobs"))
                    changes.Add($"jobs at once {Int(before, "maxConcurrentJobs")} → {Int(after, "maxConcurrentJobs")}");
                if (Bool(before, "isActive") != Bool(after, "isActive"))
                    changes.Add(Bool(after, "isActive") ? "put on sale" : "taken off sale");
                return ("plan", changes.Count > 0
                    ? $"Edited {plan}: {string.Join(", ", changes)}."
                    : $"Saved {plan} with no change to its prices or run limits.");
            }
            case Reorder:
                return ("order", after?.TryGetProperty("order", out var order) == true
                    ? $"Put the plans in this order: {string.Join(", ", order.EnumerateArray().Select(item => item.GetString()))}."
                    : "Reordered the plans.");
            default:
                return ("other", log.ActionType);
        }
    }

    private static JsonDocument? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement? element, string name) =>
        element is { } value && value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool Bool(JsonElement? element, string name) =>
        element is { } value && value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.True;

    private static int? Int(JsonElement? element, string name) =>
        element is { } value && value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;

    // "$29.99", or "none" when the plan has no such price.
    private static string Usd(JsonElement? element, string name) =>
        element is { } value && value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetDecimal().ToString("$0.##", System.Globalization.CultureInfo.InvariantCulture)
            : "none";

    // "26 Oct 2027" from "2027-10-26".
    private static string? FormatDate(string? value) =>
        DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var date)
            ? date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : value;

    private static string Limit(JsonElement? element) =>
        !Bool(element, "isEnabled") ? "off" : Int(element, "limitValue") is { } limit ? limit.ToString("N0") : "no limit";
}
