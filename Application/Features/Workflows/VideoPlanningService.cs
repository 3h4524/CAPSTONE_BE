using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows.Validators;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <param name="ForDesign">
/// Preview for one design of a batch video run: the scenes, set up on the preview product, are first rewritten to
/// this design's own mock-ups (<see cref="SceneSourceResolver"/>).
/// </param>
public sealed record PreviewStoryboardRequest(GenerateVideoConfig Config, IReadOnlyList<Guid> MockupIds, string? ArtworkGroupKey, bool ForDesign = false);
public interface IVideoPlanningService
{
    Task<Result<Storyboard>> PreviewAsync(Guid productId, PreviewStoryboardRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<VideoTemplateCatalogItem>>> ListTemplatesAsync(CancellationToken ct);
    Task<Result<IReadOnlyList<VideoPackDefinition>>> ListVideoPacksAsync(CancellationToken ct);
    Task<Result<IReadOnlyList<AiVideoTemplateDefinition>>> ListAiTemplatesAsync(CancellationToken ct);
    Task<Result<IReadOnlyList<AiCharacterCatalogItem>>> ListAiCharactersAsync(CancellationToken ct);
}
public sealed class VideoPlanningService(ICurrentUser user, IRepository<Product> products, IRepository<MockupImage> assets,
    IRepository<VideoTemplate> templates, IRepository<AiCharacterModel> characterModels,
    GenerateVideoConfigValidator validator, WorkflowCapabilityRegistry capabilities,
    VideoStoryboardPlanner planner, IVideoExperienceContextService? experiences = null,
    IVideoPromptPreparationService? promptPreparation = null, IRunTargetConfigResolver? targetResolver = null) : IVideoPlanningService
{
    public async Task<Result<Storyboard>> PreviewAsync(Guid productId, PreviewStoryboardRequest request, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(productId, ct);
        if (product is null || product.UserId != user.UserId || product.DeletedAt != null) return Result.Failure<Storyboard>(Error.NotFound("ProductNotFound", "Product not found."));
        if (request.Config is null || request.MockupIds is null) return Result.Failure<Storyboard>(Error.Validation("Video configuration and mockup selection are required."));
        if (!capabilities.IsModeEnabled(request.Config.Mode)) return Result.Failure<Storyboard>(new("UnsupportedVideoMode", "Only Standard Showcase is enabled.", ErrorType.Validation));
        // A design's storyboard is made from the batch run's scene list, whose scenes may leave their image to be
        // chosen for each design: the resolver below picks them, so the list is checked as a batch run checks it.
        var validation = request.ForDesign
            ? await validator.ValidateForBatchAsync(request.Config, ct)
            : await validator.ValidateAsync(request.Config, ct);
        if (!validation.IsValid || request.MockupIds.Count > 8) return Result.Failure<Storyboard>(Error.Validation("Invalid video configuration or mockup selection."));
        if (request.ForDesign && targetResolver is not null)
        {
            var resolved = await targetResolver.ResolveAsync(user.UserId!.Value, productId, request.ArtworkGroupKey, request.Config, null, ct);
            if (resolved.Error is not null) return Result.Failure<Storyboard>(Error.Validation(resolved.Error));
            request = request with { Config = resolved.Config, MockupIds = [] };
        }
        var owned = await assets.FindAsync(x => x.ProductId == productId && x.DeletedAt == null, ct);
        var group = VideoStoryboardPlanner.KnownArtworkGroup(owned, request.ArtworkGroupKey);
        var aiTemplates = await LoadAiTemplatesAsync(ct);
        var approved = owned.Where(x => x.ApprovalStatus == "approved" && x.ApprovedRevision == x.MetadataRevision)
            .Where(x => group == null || x.ArtworkGroupKey == group).ToArray();
        var requestedIds = new HashSet<Guid>(request.MockupIds);
        if (request.Config.SelectedMockupIds is { } selectedIds) requestedIds.UnionWith(selectedIds);
        if (request.Config.SceneOrder is { } sceneOrder) requestedIds.UnionWith(sceneOrder);
        if (request.Config.StandardScenes is { } standardScenes)
            requestedIds.UnionWith(standardScenes.Select(scene => scene.MockupId));
        if (request.Config.AiScenes is { } scenes)
        {
            foreach (var scene in scenes)
            {
                if (scene.MockupId is Guid sourceId) requestedIds.Add(sourceId);
                if (scene.StudioFrameId is Guid frameId) requestedIds.Add(frameId);
            }
        }
        if (request.Config.SharedMockupId is Guid sharedMockupId) requestedIds.Add(sharedMockupId);
        if (request.Config.AiStudioMode == "studio")
        {
            if (request.Config.AiStudioSourceMockupId is Guid legacySourceId) requestedIds.Add(legacySourceId);
            if (request.Config.AiStudioFrameId is Guid legacyFrameId) requestedIds.Add(legacyFrameId);
        }
        foreach (var frame in owned.Where(x => requestedIds.Contains(x.Id)))
        {
            var identity = AiStudioFrameIdentity.Read(frame.Regions);
            if (identity is not null) requestedIds.Add(identity.SourceMockupId);
        }
        // The automatic pick skips a composite that a newer one of the same template and color replaces; an image named
        // explicitly stays (a batch design's names were already moved to its current images by the target resolver).
        var superseded = APCS.Application.Features.BatchMockups.Common.MockupSources.Superseded(owned);
        var selected = request.MockupIds.Count == 0
            ? approved.Where(x => requestedIds.Contains(x.Id)).Concat(approved.Where(x => !superseded.Contains(x.Id)).Take(8)).DistinctBy(x => x.Id).ToList()
            : approved.Where(x => requestedIds.Contains(x.Id)).ToList();
        selected.AddRange(owned.Where(x => requestedIds.Contains(x.Id) && x.SourceType == APCS.Application.Features.BatchMockups.Common.MockupSources.AiGenerated && x.Role == "VideoScene"));
        var experienceMap = experiences is null
            ? null
            : await experiences.RetrieveForScenesAsync(user.UserId!.Value, product, request.Config, ct);
        var sources = selected.DistinctBy(x => x.Id).ToArray();
        var planned = planner.Plan(product, sources, request.Config, aiTemplates, experienceMap);
        if (planned.IsFailure || promptPreparation is null) return planned;
        return Result.Success(await promptPreparation.PrepareAsync(user.UserId!.Value, planned.Value, sources, ct));
    }

    public async Task<Result<IReadOnlyList<VideoTemplateCatalogItem>>> ListTemplatesAsync(CancellationToken ct)
    {
        var order = new[] { "product_showcase", "design_detail", "variant_showcase" };
        var rows = await templates.FindAsync(x => x.TemplateVersion == 2 && x.IsActive == true && x.IsSystemTemplate == true &&
            x.Code != null && order.Contains(x.Code), ct);
        var result = rows.OrderBy(x => Array.IndexOf(order, x.Code)).Select(x =>
        {
            var effects = SafeReadEffects(x.EffectsConfig);
            return new VideoTemplateCatalogItem(x.Code!, x.TemplateVersion, x.Name,
                effects.Description ?? x.Name,
                x.PreviewVideoUrl, x.DurationSeconds,
                effects.DefaultTransition is null or "fade" or "cut" ? "none" : effects.DefaultTransition,
                effects.DefaultMotionPreset ?? "varied",
                effects.Requirements ?? FallbackRequirements(x.Code));
        }).ToArray();
        return Result.Success<IReadOnlyList<VideoTemplateCatalogItem>>(result);
    }

    public async Task<Result<IReadOnlyList<VideoPackDefinition>>> ListVideoPacksAsync(CancellationToken ct)
    {
        var rows = await templates.FindAsync(template => template.TemplateVersion >= 1 && template.IsActive == true &&
            template.IsSystemTemplate == true && template.Type == "video_pack" && template.Code != null, ct);
        var result = rows.OrderBy(template => template.Name, StringComparer.Ordinal).Select(template =>
        {
            var pack = SafeReadPackEffects(template.EffectsConfig).VideoPack;
            return pack is null ? null : new VideoPackDefinition(template.Code!, template.TemplateVersion, template.Name,
                pack.Description ?? template.Name, template.PreviewVideoUrl, template.DurationSeconds,
                pack.RequiresCharacter, pack.RequiresSharedMockup, pack.Scenes ?? []);
        }).Where(pack => pack is not null).Cast<VideoPackDefinition>().ToArray();
        return Result.Success<IReadOnlyList<VideoPackDefinition>>(result);
    }

    public async Task<Result<IReadOnlyList<AiVideoTemplateDefinition>>> ListAiTemplatesAsync(CancellationToken ct) =>
        Result.Success(AiVideoPromptCompiler.Catalog(await LoadAiTemplatesAsync(ct)));

    public async Task<Result<IReadOnlyList<AiCharacterCatalogItem>>> ListAiCharactersAsync(CancellationToken ct)
    {
        var rows = await characterModels.FindAsync(model => model.IsActive && model.DeletedAt == null, ct);
        var result = rows.OrderBy(model => model.SortOrder).ThenBy(model => model.Name)
            .Select(model => new AiCharacterCatalogItem(model.Id, model.Code, model.Name, model.Continent,
                model.Gender, model.AgeMin, model.AgeMax, model.BodyType, model.SkinTone, model.Description,
                model.ImageUrl, model.PreviewUrl, model.IsDefault))
            .ToArray();
        return Result.Success<IReadOnlyList<AiCharacterCatalogItem>>(result);
    }

    private async Task<IReadOnlyList<VideoTemplate>> LoadAiTemplatesAsync(CancellationToken ct)
    {
        var codes = AiVideoPromptCompiler.Catalog().Select(item => item.Code).ToArray();
        return await templates.FindAsync(x => x.IsSystemTemplate == true && x.IsActive == true && x.Code != null && codes.Contains(x.Code), ct);
    }

    private static VideoTemplateEffects SafeReadEffects(string? effectsConfig)
    {
        if (string.IsNullOrWhiteSpace(effectsConfig)) return new(null, null, null, null);
        try
        {
            return WorkflowJson.Read<VideoTemplateEffects>(effectsConfig);
        }
        catch (System.Text.Json.JsonException)
        {
            return new(null, null, null, null);
        }
    }

    private static VideoPackEffects SafeReadPackEffects(string? effectsConfig)
    {
        if (string.IsNullOrWhiteSpace(effectsConfig)) return new(null);
        try
        {
            return WorkflowJson.Read<VideoPackEffects>(effectsConfig);
        }
        catch (System.Text.Json.JsonException)
        {
            return new(null);
        }
    }

    private static VideoTemplateRequirements FallbackRequirements(string? code) => code switch
    {
        "design_detail" => new(1, true, 0),
        "variant_showcase" => new(2, false, 2),
        _ => new(1, false, 0),
    };

    private sealed record VideoTemplateEffects(string? Description, string? DefaultTransition,
        string? DefaultMotionPreset, VideoTemplateRequirements? Requirements);
    private sealed record VideoPackEffects(VideoPackPayload? VideoPack);
    private sealed record VideoPackPayload(string? Description, bool RequiresCharacter, bool RequiresSharedMockup,
        IReadOnlyList<VideoPackSceneDefinition>? Scenes);
}
