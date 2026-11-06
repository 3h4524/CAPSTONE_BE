using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Application.Features.Entitlements;

/// <summary>
/// What an Admin has set for Free and the free trial: their allowances, and the plan the trial
/// gives. Read from <c>usage_allowances</c> and <c>plan_settings</c>; whatever has no row falls back
/// to <see cref="UsageAllowanceOptions"/> and to <see cref="PlanSettingKeys.DefaultTrialPlanTier"/>.
/// </summary>
public interface IPlanSettings
{
    /// <summary>Free's monthly allowances and the trial's, as an Admin set them.</summary>
    Task<UsageAllowanceOptions> GetAllowancesAsync(CancellationToken cancellationToken = default);

    /// <summary>The tier of the plan the free trial gives.</summary>
    Task<string> GetTrialPlanTierAsync(CancellationToken cancellationToken = default);

    /// <summary>The tier of the plan Sellers see marked Recommended; null when an Admin chose none.</summary>
    Task<string?> GetRecommendedPlanTierAsync(CancellationToken cancellationToken = default);

    /// <summary>How many days a new free trial lasts. Trials already running keep their own end.</summary>
    Task<int> GetTrialDaysAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the cached settings, so the next read sees an Admin's edit.</summary>
    void Invalidate();
}

/// <summary>The <c>plan_settings</c> keys.</summary>
public static class PlanSettingKeys
{
    public const string TrialPlanTier = "trial_plan_tier";

    /// <summary>The plan the trial gives when no Admin has chosen one.</summary>
    public const string DefaultTrialPlanTier = "pro";

    public const string RecommendedPlanTier = "recommended_plan_tier";

    /// <summary>The plan marked Recommended when no Admin has chosen one. A row with an empty value means none.</summary>
    public const string DefaultRecommendedPlanTier = "pro";

    public const string TrialDays = "trial_days";

    /// <summary>How long a trial lasts when no Admin has set it.</summary>
    public const int DefaultTrialDays = 14;

    /// <summary>The longest trial an Admin can set.</summary>
    public const int MaxTrialDays = 90;
}

/// <summary>
/// Every allowance an Admin can set, with the <c>usage_allowances</c> row it is stored in and the
/// <see cref="UsageAllowanceOptions"/> property it fills.
/// </summary>
public static class AllowanceSettings
{
    public const string ScopeFree = UsageAllowances.ScopeFree;
    public const string ScopeTrial = UsageAllowances.ScopeTrial;
    public const string ProductsPerRun = "products_per_run";

    /// <summary>One allowance: where it is stored, what it is called, and how it maps to the options.</summary>
    public sealed record Definition(
        string Scope,
        string Code,
        string Label,
        Func<UsageAllowanceOptions, int> Get,
        Action<UsageAllowanceOptions, int> Set);

    /// <summary>Every allowance, Free's first, in the order the Admin sees them.</summary>
    public static readonly IReadOnlyList<Definition> All =
    [
        new(ScopeFree, UsageAllowances.FullRuns, "Full runs (with a video)", options => options.Free.FullRuns, (options, value) => options.Free.FullRuns = value),
        new(ScopeFree, UsageAllowances.DesignRuns, "Design runs", options => options.Free.DesignRuns, (options, value) => options.Free.DesignRuns = value),
        new(ScopeFree, UsageAllowances.DesignImages, "Design images", options => options.Free.DesignImages, (options, value) => options.Free.DesignImages = value),
        new(ScopeFree, UsageAllowances.Mockups, "Mockups", options => options.Free.Mockups, (options, value) => options.Free.Mockups = value),
        new(ScopeFree, UsageAllowances.Videos, "Standard videos", options => options.Free.Videos, (options, value) => options.Free.Videos = value),
        new(ScopeFree, UsageAllowances.Listings, "Listings", options => options.Free.Listings, (options, value) => options.Free.Listings = value),
        new(ScopeTrial, ProductsPerRun, "Products per run", options => options.Trial.ProductsPerRun, (options, value) => options.Trial.ProductsPerRun = value),
        new(ScopeTrial, UsageAllowances.Products, "Products", options => options.Trial.Products, (options, value) => options.Trial.Products = value),
        new(ScopeTrial, UsageAllowances.DesignImages, "Design images", options => options.Trial.DesignImages, (options, value) => options.Trial.DesignImages = value),
        new(ScopeTrial, UsageAllowances.Mockups, "Mockups", options => options.Trial.Mockups, (options, value) => options.Trial.Mockups = value),
        new(ScopeTrial, UsageAllowances.Videos, "Standard videos", options => options.Trial.Videos, (options, value) => options.Trial.Videos = value),
        new(ScopeTrial, UsageAllowances.Listings, "Listings", options => options.Trial.Listings, (options, value) => options.Trial.Listings = value),
    ];

    /// <summary>A copy of <paramref name="source"/>, so the configured defaults are never changed.</summary>
    public static UsageAllowanceOptions Copy(UsageAllowanceOptions source)
    {
        var copy = new UsageAllowanceOptions();
        foreach (var definition in All) definition.Set(copy, definition.Get(source));
        return copy;
    }
}

/// <summary>
/// Keeps the settings between requests. They change only when an Admin edits them, which calls
/// <see cref="IPlanSettings.Invalidate"/>; the expiry covers an edit made on another server.
/// </summary>
public sealed class PlanSettingsCache(TimeProvider timeProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    // Swapped whole, so requests on other threads see either the old settings or the new ones.
    private volatile Snapshot? current;

    /// <summary>Gets the cached settings, or null when there are none or they have expired.</summary>
    public PlanSettingsSnapshot? Get() =>
        current is { } cached && timeProvider.GetUtcNow() - cached.LoadedAt < Lifetime ? cached.Settings : null;

    /// <summary>Caches freshly read settings.</summary>
    public void Set(PlanSettingsSnapshot settings) => current = new Snapshot(settings, timeProvider.GetUtcNow());

    /// <summary>Drops the cached settings.</summary>
    public void Clear() => current = null;

    private sealed record Snapshot(PlanSettingsSnapshot Settings, DateTimeOffset LoadedAt);
}

/// <summary>Everything read from the settings tables at once.</summary>
public sealed record PlanSettingsSnapshot(UsageAllowanceOptions Allowances, string TrialPlanTier, string? RecommendedPlanTier, int TrialDays);

/// <inheritdoc />
public sealed class PlanSettings(
    IRepository<UsageAllowance> allowances,
    IRepository<PlanSetting> settings,
    IOptions<UsageAllowanceOptions> defaults,
    PlanSettingsCache cache,
    ILogger<PlanSettings> logger) : IPlanSettings
{
    /// <inheritdoc />
    public async Task<UsageAllowanceOptions> GetAllowancesAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).Allowances;

    /// <inheritdoc />
    public async Task<string> GetTrialPlanTierAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).TrialPlanTier;

    /// <inheritdoc />
    public async Task<string?> GetRecommendedPlanTierAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).RecommendedPlanTier;

    /// <inheritdoc />
    public async Task<int> GetTrialDaysAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).TrialDays;

    /// <inheritdoc />
    public void Invalidate() => cache.Clear();

    private async Task<PlanSettingsSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        if (cache.Get() is { } cached) return cached;

        var result = AllowanceSettings.Copy(defaults.Value);
        var trialPlanTier = PlanSettingKeys.DefaultTrialPlanTier;
        string? recommendedPlanTier = PlanSettingKeys.DefaultRecommendedPlanTier;
        var trialDays = PlanSettingKeys.DefaultTrialDays;
        try
        {
            var rows = await allowances.Query().AsNoTracking().ToListAsync(cancellationToken);
            foreach (var definition in AllowanceSettings.All)
            {
                if (rows.FirstOrDefault(row => row.Scope == definition.Scope && row.Code == definition.Code) is { } row)
                    definition.Set(result, row.LimitValue);
            }

            var stored = await settings.Query().AsNoTracking()
                .Where(setting => setting.Key == PlanSettingKeys.TrialPlanTier
                    || setting.Key == PlanSettingKeys.RecommendedPlanTier
                    || setting.Key == PlanSettingKeys.TrialDays)
                .Select(setting => new { setting.Key, setting.Value })
                .ToListAsync(cancellationToken);
            var tier = stored.FirstOrDefault(setting => setting.Key == PlanSettingKeys.TrialPlanTier)?.Value;
            if (!string.IsNullOrWhiteSpace(tier)) trialPlanTier = tier;
            // A row with an empty value is an Admin's choice of no recommended plan.
            if (stored.FirstOrDefault(setting => setting.Key == PlanSettingKeys.RecommendedPlanTier) is { } recommended)
                recommendedPlanTier = string.IsNullOrWhiteSpace(recommended.Value) ? null : recommended.Value;
            if (int.TryParse(stored.FirstOrDefault(setting => setting.Key == PlanSettingKeys.TrialDays)?.Value, out var days)
                && days is >= 1 and <= PlanSettingKeys.MaxTrialDays)
                trialDays = days;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Not cached, so the tables are tried again on the next read.
            logger.LogError(exception, "Could not read the plan settings; using the configured defaults.");
            return new PlanSettingsSnapshot(result, trialPlanTier, recommendedPlanTier, trialDays);
        }

        var snapshot = new PlanSettingsSnapshot(result, trialPlanTier, recommendedPlanTier, trialDays);
        cache.Set(snapshot);
        return snapshot;
    }
}
