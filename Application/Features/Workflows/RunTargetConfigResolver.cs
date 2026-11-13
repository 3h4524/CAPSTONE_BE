using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Loads one design's approved mock-ups and rewrites a video configuration to use them.</summary>
public interface IRunTargetConfigResolver
{
    Task<ResolvedVideoConfig> ResolveAsync(Guid userId, Guid productId, string? artworkGroupKey, GenerateVideoConfig config,
        TargetOverride? overrides, CancellationToken ct);

    /// <summary>
    /// Rewrites the settings sent to a child run of a run group (a retry sends the canvas, which shows the shared
    /// scene list) for that run's design, keeping the images the seller picked for it.
    /// </summary>
    Task<ResolvedVideoConfig> ResolveForRunAsync(WorkflowRun run, GenerateVideoConfig config, CancellationToken ct);

    /// <summary>The design's mock-ups approved at their current revision, keyframes included.</summary>
    Task<IReadOnlyList<MockupImage>> ApprovedAsync(Guid productId, string? artworkGroupKey, CancellationToken ct);
}

public sealed class RunTargetConfigResolver(IRepository<MockupImage> assets, IRepository<Product> products,
    IRepository<WorkflowRunGroup> groups) : IRunTargetConfigResolver
{
    public static string TargetKey(Guid productId, string artworkGroupKey) => $"{productId:D}:{artworkGroupKey}";

    public static IReadOnlyDictionary<string, TargetOverride> ReadOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, TargetOverride>();
        try { return WorkflowJson.Read<Dictionary<string, TargetOverride>>(json) ?? new Dictionary<string, TargetOverride>(); }
        catch (System.Text.Json.JsonException) { return new Dictionary<string, TargetOverride>(); }
    }

    public async Task<ResolvedVideoConfig> ResolveForRunAsync(WorkflowRun run, GenerateVideoConfig config, CancellationToken ct)
    {
        TargetOverride? overrides = null;
        if (run.RunGroupId is Guid groupId && run.ArtworkGroupKey is { } key && await groups.GetByIdAsync(groupId, ct) is { } group)
            ReadOverrides(group.TargetOverrides).TryGetValue(TargetKey(run.ProductId, key), out overrides);
        return await ResolveAsync(run.UserId, run.ProductId, run.ArtworkGroupKey, config, overrides, ct);
    }

    public async Task<ResolvedVideoConfig> ResolveAsync(Guid userId, Guid productId, string? artworkGroupKey, GenerateVideoConfig config,
        TargetOverride? overrides, CancellationToken ct)
    {
        var approved = await ApprovedAsync(productId, artworkGroupKey, ct);
        var referenced = await ReferencedAsync(userId, config, ct);
        return SceneSourceResolver.Resolve(config, approved, referenced, overrides);
    }

    public async Task<IReadOnlyList<MockupImage>> ApprovedAsync(Guid productId, string? artworkGroupKey, CancellationToken ct)
    {
        // Every stored row takes part in finding the replaced ones: a newer composite still waiting for review replaces an
        // approved older one, so the design waits for that review instead of rendering the outdated image.
        var stored = await assets.FindAsync(x => x.ProductId == productId && x.DeletedAt == null && x.ContentHash != null, ct);
        var owned = MockupSources.Current(stored).Where(x => x.ApprovalStatus == "approved" && x.ApprovedRevision == x.MetadataRevision).ToArray();
        return string.IsNullOrEmpty(artworkGroupKey)
            ? owned
            : owned.Where(x => (x.ArtworkGroupKey ?? x.ProductId.ToString()) == artworkGroupKey).ToArray();
    }

    /// <summary>The mock-ups the configuration names, limited to the seller's own products.</summary>
    private async Task<IReadOnlyDictionary<Guid, MockupImage>> ReferencedAsync(Guid userId, GenerateVideoConfig config, CancellationToken ct)
    {
        var ids = (config.AiScenes ?? []).Select(scene => scene.MockupId).OfType<Guid>()
            .Concat(config.SelectedMockupIds ?? []).Concat(config.SceneOrder ?? [])
            .Concat((config.StandardScenes ?? []).Select(scene => scene.MockupId))
            .Concat(new[] { config.SharedMockupId, config.AiStudioSourceMockupId, config.AiShotOptions?.MockupId }.OfType<Guid>())
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, MockupImage>();
        var found = await assets.FindAsync(x => ids.Contains(x.Id) && x.DeletedAt == null, ct);
        var productIds = found.Select(x => x.ProductId).Distinct().ToArray();
        var owned = (await products.FindAsync(x => productIds.Contains(x.Id) && x.UserId == userId, ct)).Select(x => x.Id).ToHashSet();
        return found.Where(x => owned.Contains(x.ProductId)).ToDictionary(x => x.Id);
    }
}
