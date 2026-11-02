using APCS.Application.Abstractions.Persistence;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Entitlements;

/// <summary>
/// The plan features as an Admin has set them up: label, description, group, order, whether they
/// are offered when adding features to a plan, and whether the free trial includes them.
/// </summary>
/// <remarks>
/// Only codes in <see cref="PlanFeatures.All"/> are listed, since only those are checked in code,
/// and each keeps its kind from there. A code with no <c>features</c> row falls back to its
/// definition in code, so a database without the table still works.
/// </remarks>
public interface IFeatureCatalog
{
    /// <summary>Gets every feature, in display order.</summary>
    Task<IReadOnlyList<FeatureCatalogEntry>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds a feature, or null when the code is not one the backend checks.</summary>
    Task<FeatureCatalogEntry?> FindAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Drops the cached catalog, so the next read sees an Admin's edit.</summary>
    void Invalidate();
}

/// <summary>One plan feature as the catalog shows it.</summary>
public sealed record FeatureCatalogEntry(
    string Code,
    string Label,
    string Description,
    string Group,
    string Kind,
    string? Unit,
    int? DefaultLimit,
    bool TrialAvailable,
    bool IsActive,
    int SortOrder,
    DateTime? UpdatedAt,
    Guid? UpdatedBy)
{
    /// <summary>The entry for a code defined in code only, with no <c>features</c> row.</summary>
    public static FeatureCatalogEntry FromDefinition(PlanFeatureDefinition definition) =>
        // Spaced like the seed in plan-feature-catalog.sql: 10, 20, 30…
        new(
            definition.Code,
            definition.Label,
            definition.Description,
            definition.Group,
            definition.Kind,
            null,
            null,
            !PlanFeatures.TrialExcluded.Contains(definition.Code),
            true,
            (PlanFeatures.All.ToList().IndexOf(definition) + 1) * 10,
            null,
            null);

    /// <summary>The catalog as defined in code, for tests and as the fallback.</summary>
    public static IReadOnlyList<FeatureCatalogEntry> Defaults { get; } = PlanFeatures.All.Select(FromDefinition).ToList();
}

/// <summary>
/// Keeps the catalog between requests. It changes only when an Admin edits it, which calls
/// <see cref="IFeatureCatalog.Invalidate"/>; the expiry covers an edit made on another server.
/// </summary>
public sealed class FeatureCatalogCache(TimeProvider timeProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    // Swapped whole, so requests on other threads see either the old catalog or the new one.
    private volatile Snapshot? current;

    /// <summary>Gets the cached catalog, or null when there is none or it has expired.</summary>
    public IReadOnlyList<FeatureCatalogEntry>? Get() =>
        current is { } cached && timeProvider.GetUtcNow() - cached.LoadedAt < Lifetime ? cached.Entries : null;

    /// <summary>Caches a freshly read catalog.</summary>
    public void Set(IReadOnlyList<FeatureCatalogEntry> entries) => current = new Snapshot(entries, timeProvider.GetUtcNow());

    /// <summary>Drops the cached catalog.</summary>
    public void Clear() => current = null;

    private sealed record Snapshot(IReadOnlyList<FeatureCatalogEntry> Entries, DateTimeOffset LoadedAt);
}

/// <inheritdoc />
public sealed class FeatureCatalog(
    IRepository<Feature> features,
    FeatureCatalogCache cache,
    ILogger<FeatureCatalog> logger) : IFeatureCatalog
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<FeatureCatalogEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (cache.Get() is { } cached) return cached;

        List<Feature> rows;
        try
        {
            rows = await features.Query().AsNoTracking().ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Not cached, so the table is tried again on the next read.
            logger.LogError(exception, "Could not read the features table; using the plan features defined in code.");
            return FeatureCatalogEntry.Defaults;
        }

        var byCode = rows.ToDictionary(row => row.Code, StringComparer.Ordinal);
        var missing = PlanFeatures.All.Where(definition => !byCode.ContainsKey(definition.Code)).Select(definition => definition.Code).ToList();
        if (missing.Count > 0)
            logger.LogWarning("The features table has no row for {Codes}; using their definitions in code. Run scripts/sql/plan-feature-catalog.sql.", string.Join(", ", missing));

        var entries = PlanFeatures.All
            .Select(definition => byCode.TryGetValue(definition.Code, out var row)
                ? new FeatureCatalogEntry(
                    row.Code,
                    row.Label,
                    row.Description,
                    row.GroupName,
                    definition.Kind,
                    row.Unit,
                    definition.Kind == PlanFeatureKinds.Limit ? row.DefaultLimit : null,
                    row.TrialAvailable,
                    row.IsActive,
                    row.SortOrder,
                    row.UpdatedAt,
                    row.UpdatedBy)
                : FeatureCatalogEntry.FromDefinition(definition))
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Code, StringComparer.Ordinal)
            .ToList();

        cache.Set(entries);
        return entries;
    }

    /// <inheritdoc />
    public async Task<FeatureCatalogEntry?> FindAsync(string code, CancellationToken cancellationToken = default) =>
        (await GetAllAsync(cancellationToken)).FirstOrDefault(entry => entry.Code == code);

    /// <inheritdoc />
    public void Invalidate() => cache.Clear();
}
