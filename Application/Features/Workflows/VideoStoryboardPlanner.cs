using APCS.Common.Models;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public sealed class VideoStoryboardPlanner
{
    private const int TransitionFrames = 12;
    private const double PushPullScale = 1.1d;
    public static readonly string[] ProductTypes = ["tshirt", "t-shirt", "t_shirt", "hoodie", "mug", "poster", "tote_bag", "phone_case"];
    /// <summary>
    /// The artwork group to narrow a product's mock-ups to, or <see langword="null"/> for no narrowing.
    /// </summary>
    /// <remarks>
    /// A workflow keeps the group chosen for the product it was last edited with. A run on another
    /// product, or one whose design was generated during the run, has no mock-up in that group, and
    /// filtering by it would hide every mock-up the seller just approved.
    /// </remarks>
    public static string? KnownArtworkGroup(IReadOnlyList<MockupImage> owned, string? configured) =>
        !string.IsNullOrEmpty(configured) && owned.Any(x => x.ArtworkGroupKey == configured) ? configured : null;

    public Result<Storyboard> Plan(Product product, IReadOnlyList<MockupImage> assets, GenerateVideoConfig config,
        IReadOnlyList<VideoTemplate>? aiVideoTemplates = null,
        IReadOnlyDictionary<int, IReadOnlyList<VideoPromptExperience>>? experiences = null)
    {
        if (!ProductTypes.Contains(product.ProductType.ToLowerInvariant())) return Result.Failure<Storyboard>(Error.Validation("Unsupported product type."));
        if (!ValidVideoEdit(config.VideoEdit)) return Result.Failure<Storyboard>(Error.Validation("Video text or music settings are invalid."));
        if (config.Mode == "ai_shot" && config.OutputFormat == "square" &&
            (config.AiScenes is not { Count: > 0 } || config.AiScenes.Any(scene => scene.Mode != "standard")))
            return Result.Failure<Storyboard>(Error.Validation("AI video supports portrait or landscape output."));
        var eligible = assets.Where(x => x.ProductId == product.Id && x.DeletedAt == null && x.ApprovalStatus == "approved" &&
            x.ApprovedRevision == x.MetadataRevision && !string.IsNullOrEmpty(x.ContentHash)).ToList();
        var hasSceneTimeline = config.AiScenes is { Count: > 0 } &&
            (config.Mode == "ai_shot" || config.AiScenes.All(scene => scene.Mode != "standard" || scene.MockupId.HasValue));
        if (!hasSceneTimeline && config.AssetSelection == "manual") eligible = eligible.Where(x => config.SelectedMockupIds!.Contains(x.Id)).ToList();
        if (eligible.Count == 0) return Result.Failure<Storyboard>(Error.Conflict("WaitingForInput", "Approve at least one mockup at its current revision."));
        if (eligible.Select(x => x.ArtworkGroupKey).Distinct().Count() != 1 || string.IsNullOrWhiteSpace(eligible[0].ArtworkGroupKey))
            return Result.Failure<Storyboard>(Error.Validation("All selected mockups must share an artwork group."));
        // The scene editor is also the Standard editor. Use it only once each
        // Standard scene has a source image; its initial blank scene is a UI draft
        // and must not prevent the approved mockups from continuing automatically.
        if (hasSceneTimeline)
            return PlanAiScenes(product, eligible, config, aiVideoTemplates, experiences);
        var templateVersion = config.TemplateVersion ?? 1;
        var output = VideoOutputFormats.Resolve(config.OutputFormat);
        var template = config.Mode == "ai_shot" && config.AiShotOptions?.Coverage == "full_video"
            ? "product_showcase" : config.Mode == "standard"
                // Product Showcase is retained only as the internal, persisted template
                // identifier. It no longer selects or constrains Standard scenes.
                ? "product_showcase"
                : ResolveTemplate(config.Template, eligible, templateVersion);
        if (templateVersion >= 2 && template == "design_detail" &&
            !eligible.Any(x => x.Role == "ArtworkDetail" || WorkflowJson.Read<MockupRegions>(x.Regions).Detail != null))
            return Result.Failure<Storyboard>(Error.Validation("Design Detail requires an artwork-detail image or a marked detail region."));
        if (templateVersion >= 2 && template == "variant_showcase" &&
            eligible.Select(x => x.VariantKey).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Count() < 2)
            return Result.Failure<Storyboard>(Error.Validation("Variant Showcase requires at least two named variants."));
        var roles = template switch
        {
            "design_detail" => new[] { "Hero", "ArtworkDetail", "AlternativeAngle", "Lifestyle" },
            "variant_showcase" => new[] { "Hero", "Variant", "AlternativeAngle", "Lifestyle" },
            _ => new[] { "Hero", "AlternativeAngle", "Lifestyle", "ArtworkDetail" }
        };
        var preferred = eligible
            .OrderBy(x => template == "design_detail" && HasDetail(x) ? 0 : 1)
            .ThenBy(x => Array.IndexOf(roles, x.Role) < 0 ? 99 : Array.IndexOf(roles, x.Role))
            .ThenBy(x => x.VariantKey, StringComparer.Ordinal)
            .ThenByDescending(x => (long)x.MockupWidthPx * x.MockupHeightPx)
            .ThenBy(x => x.Id)
            .ToList();
        var ordered = templateVersion >= 2 && template == "variant_showcase"
            ? preferred.GroupBy(x => string.IsNullOrWhiteSpace(x.VariantKey) ? x.Id.ToString() : x.VariantKey!, StringComparer.Ordinal)
                .Select(x => x.First()).Concat(preferred).DistinctBy(x => x.Id).Take(4).ToList()
            : preferred.Take(4).ToList();
        if (config.SceneOrder is { Count: > 0 })
        {
            if (config.SceneOrder.Any(id => eligible.All(x => x.Id != id))) return Result.Failure<Storyboard>(Error.Validation("Scene order contains an unavailable mockup."));
            ordered = config.SceneOrder.Select(id => eligible.Single(x => x.Id == id)).ToList();
        }
        if (config.Mode == "standard" && config.StandardScenes is { Count: > 0 })
        {
            if (config.StandardScenes.Any(scene => ordered.All(asset => asset.Id != scene.MockupId)))
                return Result.Failure<Storyboard>(Error.Validation("Custom framing refers to a mockup that is not in the scene order."));
            foreach (var scene in config.StandardScenes)
            {
                if (!ValidRegion(scene.BaseCrop) || !ValidRegion(scene.StartCrop) || !ValidRegion(scene.EndCrop) ||
                    !ValidScale(scene.StartScale) || !ValidScale(scene.EndScale) ||
                    !ValidRotation(scene.StartRotation) || !ValidRotation(scene.EndRotation))
                    return Result.Failure<Storyboard>(Error.Validation("Custom scene framing has an invalid crop or transform value."));
            }
        }
        if (ordered.Count == 1) ordered.Add(ordered[0]);
        var outputFrames = config.DurationSeconds * 30;
        var transition = NormalizeTransition(config.StandardOptions?.Transition ?? "none");
        var overlapFrames = transition == "none" ? 0 : TransitionFrames;
        // A transition is composited over the boundary between two scene slots. It must not add
        // frames to either slot or shorten the exported video.
        var totalFrames = outputFrames;
        var scenes = ordered.Select((asset, index) => CreateScene(asset, product.ProductType, config, index,
            totalFrames / ordered.Count + (index < totalFrames % ordered.Count ? 1 : 0),
            config.Mode == "standard" ? config.StandardScenes?.SingleOrDefault(scene => scene.MockupId == asset.Id) : null) with
            { Transition = index == 0 ? "none" : transition, TransitionFrames = index == 0 || transition == "none" ? 0 : TransitionFrames }).ToArray();
        if (templateVersion >= 2 && ordered.Count == 2 && ordered[0].Id == ordered[1].Id &&
            scenes[0].Motion == scenes[1].Motion && config.SceneMotionPresets is not { Count: > 0 } &&
            config.StandardScenes is not { Count: > 0 })
            scenes[1] = scenes[1] with
            {
                Motion = scenes[0].Motion == "static" ? "contain_gentle" : "static",
                EndCrop = scenes[1].Crop,
                EndScale = scenes[1].StartScale,
                EndRotation = scenes[1].StartRotation,
            };
        var rendererVersion = templateVersion >= 2 ? "standard-v2" : "standard-v1";
        var fingerprint = WorkflowJson.Hash(WorkflowJson.Write(new { product.Id, config, template, templateVersion, rendererVersion, scenes }));
        var standard = new Storyboard(template, templateVersion, config.DurationSeconds, product.ProductType, scenes, fingerprint,
            output.Format, output.Width, output.Height, output.AspectRatio, "standard", overlapFrames, null, outputFrames, config.VideoEdit);
        return config.Mode == "ai_shot" ? PlanAiShot(standard, eligible, config) : Result.Success(standard);
    }

    private Result<Storyboard> PlanAiScenes(Product product, IReadOnlyList<MockupImage> eligible, GenerateVideoConfig config,
        IReadOnlyList<VideoTemplate>? aiVideoTemplates,
        IReadOnlyDictionary<int, IReadOnlyList<VideoPromptExperience>>? experiences)
    {
        var requestedScenes = config.AiScenes!;
        if (requestedScenes.Count is < 1 or > 4)
            return Result.Failure<Storyboard>(Error.Validation("Configure between one and four scenes."));
        if (requestedScenes.Any(scene => scene.Mode == "standard"
                ? scene.DurationSeconds is < 1 or > 30
                : scene.DurationSeconds is not (4 or 6 or 8)) || requestedScenes.Sum(scene => scene.DurationSeconds) > 30)
            return Result.Failure<Storyboard>(Error.Validation("Standard scenes must be 1–30 seconds, AI scenes must be 4, 6 or 8 seconds, and total duration cannot exceed 30 seconds."));
        if (requestedScenes.Where((scene, index) => !ValidTimelineEdit(scene, index)).Any() || !ValidVideoEdit(config.VideoEdit))
            return Result.Failure<Storyboard>(Error.Validation("Video edits must keep each scene at least one second and use valid text, music and transitions."));
        if (requestedScenes.Any(scene => scene.Mode != "standard") && config.AiGlobalInstructions is { Length: > 500 })
            return Result.Failure<Storyboard>(Error.Validation("Keep the shared AI scene direction under 500 characters."));
        if (config.AiStudioMode is not ("direct" or "studio"))
            return Result.Failure<Storyboard>(Error.Validation("Choose direct mock-up or a studio keyframe."));

        var productSources = eligible.ToList();
        if (productSources.Count == 0)
            return Result.Failure<Storyboard>(Error.Conflict("WaitingForInput", "Approve at least one original product mock-up at its current revision."));

        var templates = AiVideoPromptCompiler.Catalog(aiVideoTemplates);
        var plannedScenes = new List<ScenePlan>(requestedScenes.Count);
        var shots = new List<AiShotPlan>(requestedScenes.Count);
        for (var index = 0; index < requestedScenes.Count; index++)
        {
            var requested = requestedScenes[index];
            if (requested.Mode is not ("ai" or "standard"))
                return Result.Failure<Storyboard>(Error.Validation($"Choose AI video or Standard motion for scene {index + 1}."));
            if (requested.Mode == "standard")
            {
                if (requested.MockupId is not Guid selectedStandardMockupId || selectedStandardMockupId == Guid.Empty)
                    return Result.Failure<Storyboard>(Error.Validation($"Choose an approved product image or keyframe for scene {index + 1}."));
                var selectedSource = productSources.FirstOrDefault(asset => asset.Id == selectedStandardMockupId);
                if (selectedSource == null)
                    return Result.Failure<Storyboard>(Error.Validation($"Scene {index + 1} source is not an approved mockup."));
                if (requested.StartFrameMode is not (null or "direct" or "studio"))
                    return Result.Failure<Storyboard>(Error.Validation($"Scene {index + 1} has an unsupported frame source."));

                var standardSource = selectedSource;
                if (requested.StartFrameMode == "studio")
                {
                    if (requested.StudioFrameId is not Guid selectedFrameId ||
                        productSources.FirstOrDefault(asset => asset.Id == selectedFrameId) is not { } selectedFrame ||
                        AiStudioFrameIdentity.Read(selectedFrame.Regions) is null)
                        return Result.Failure<Storyboard>(Error.Conflict("StudioFrameRequired", $"Choose an approved keyframe for scene {index + 1}."));
                    standardSource = selectedFrame;
                }

                var motion = requested.StandardMotionPreset ?? "zoom";
                if (motion is not ("gentle" or "contain_gentle" or "static" or "pan" or "zoom" or "zoom_out" or "pan_left" or "pan_up" or "pan_down" or
                    "diagonal_up_right" or "diagonal_up_left" or "diagonal_down_right" or "diagonal_down_left" or "rotate_cw" or "rotate_ccw" or "rotate_zoom_cw" or "rotate_zoom_ccw" or "custom"))
                    return Result.Failure<Storyboard>(Error.Validation($"Scene {index + 1} uses an unsupported Standard camera movement."));
                if (!ValidRegion(requested.StandardBaseCrop) || !ValidRegion(requested.StandardStartCrop) || !ValidRegion(requested.StandardEndCrop) ||
                    !ValidScale(requested.StandardStartScale) || !ValidScale(requested.StandardEndScale) ||
                    !ValidRotation(requested.StandardStartRotation) || !ValidRotation(requested.StandardEndRotation))
                    return Result.Failure<Storyboard>(Error.Validation("Standard camera framing has an invalid crop or transform value."));

                var sceneMotions = (config.SceneMotionPresets ?? []).ToList();
                while (sceneMotions.Count <= index) sceneMotions.Add(null);
                sceneMotions[index] = motion;
                var standardConfig = config with { SceneMotionPresets = sceneMotions };
                var customFraming = requested.StandardStartCrop != null || requested.StandardEndCrop != null || requested.StandardBaseCrop != null ||
                    requested.StandardStartScale != null || requested.StandardEndScale != null ||
                    requested.StandardStartRotation != null || requested.StandardEndRotation != null
                    ? new StandardSceneOptions(standardSource.Id, requested.StandardStartCrop, requested.StandardEndCrop, requested.StandardBaseCrop,
                        requested.StandardStartScale ?? 1, requested.StandardEndScale ?? 1, requested.StandardStartRotation ?? 0, requested.StandardEndRotation ?? 0)
                    : null;
                var standardTransition = ResolveSceneTransition(requested, index, config.StandardOptions?.Transition);
                var sourceFrames = requested.DisplayDurationFrames ?? requested.DurationSeconds * 30;
                var standardTrimStart = requested.TrimStartFrames ?? 0;
                var standardTrimEnd = requested.TrimEndFrames ?? 0;
                var standardScene = TrimStandardScene(
                    CreateScene(standardSource, product.ProductType, standardConfig, index, sourceFrames, customFraming),
                    sourceFrames,
                    standardTrimStart,
                    standardTrimEnd);
                plannedScenes.Add(standardScene with
                {
                    Transition = standardTransition, TransitionFrames = standardTransition == "none" ? 0 : requested.TransitionDurationFrames ?? TransitionFrames,
                });
                continue;
            }

            var definition = templates.SingleOrDefault(item => item.Code == requested.TemplateCode);
            if (definition == null)
                return Result.Failure<Storyboard>(Error.Validation($"AI scene {index + 1} uses an unavailable template."));
            if (requested.PromptRuleVersion is not null and not AiVideoPromptRules.CurrentVersion)
                return Result.Failure<Storyboard>(Error.Validation($"AI scene {index + 1} uses an unsupported prompt-rule version."));
            var usesP1Rules = AiVideoPromptRules.IsRequested(requested);
            if (usesP1Rules && !AiVideoPromptRules.Supports(definition))
                return Result.Failure<Storyboard>(Error.Validation($"AI scene {index + 1} can use P1 prompt rules only with the Lifestyle In-Use template."));
            if (usesP1Rules && !HasP1Brief(requested))
                return Result.Failure<Storyboard>(Error.Validation($"AI scene {index + 1} needs a start state, one primary action, and an end state of 1–240 characters for P1 prompt rules."));
            var settings = AiVideoPromptCompiler.ApplyDefaults(definition, requested);
            var ruleApplication = AiVideoPromptRules.Apply(definition, settings);
            settings = ruleApplication.Settings;
            if (!SettingsAreSupported(definition, settings))
                return Result.Failure<Storyboard>(Error.Validation($"AI scene {index + 1} contains a setting that its template does not support."));
            if (settings.ExtraInstructions is { Length: > 500 })
                return Result.Failure<Storyboard>(Error.Validation("Keep each scene's extra instructions under 500 characters."));

            var startFrameMode = requested.StartFrameMode ??
                (definition.Code == "cinematic_product_showcase" && config.AiStudioMode == "studio" ? "studio" : "direct");
            if (startFrameMode is not ("direct" or "studio"))
                return Result.Failure<Storyboard>(Error.Validation($"AI scene {index + 1} has an unsupported start-frame mode."));

            if (requested.MockupId is not Guid selectedMockupId || selectedMockupId == Guid.Empty)
                return Result.Failure<Storyboard>(Error.Validation($"Choose an approved product image or keyframe for AI scene {index + 1}."));
            var source = productSources.FirstOrDefault(asset => asset.Id == selectedMockupId);
            if (source == null)
                return Result.Failure<Storyboard>(Error.Validation($"AI scene {index + 1} source is not an approved mockup."));

            if (usesP1Rules && startFrameMode != "direct")
                return Result.Failure<Storyboard>(Error.Validation($"AI scene {index + 1} must use its approved Lifestyle image directly with P1 prompt rules."));
            if (usesP1Rules && source.Role != "Lifestyle")
                return Result.Failure<Storyboard>(Error.Validation($"AI scene {index + 1} needs an approved Lifestyle image with a person for P1 prompt rules."));

            if (definition.RequiresLifestyle && !settings.RightsConfirmed)
                return Result.Failure<Storyboard>(Error.Validation($"AI scene {index + 1} requires confirmation that you have rights to use the selected image with people."));

            var regions = WorkflowJson.Read<MockupRegions>(source.Regions);
            var detailRegions = new[] { regions.Detail, regions.Artwork }.Where(region => region != null).Cast<Region>().ToArray();
            var useStudioFrame = startFrameMode == "studio";
            var sourceCrop = definition.RequiresDetail && !useStudioFrame
                ? detailRegions.FirstOrDefault(region => Math.Min(region.Width * source.MockupWidthPx, region.Height * source.MockupHeightPx) >= 512)
                : null;

            MockupImage? studioFrame = null;
            MockupImage? studioSource = null;
            if (startFrameMode == "studio")
            {
                var layout = requested.StudioLayout ?? (requested.StartFrameMode == null ? config.AiStudioLayout : "hanging_window");
                var frameId = requested.StudioFrameId ?? (requested.StartFrameMode == null ? config.AiStudioFrameId : null);
                if (frameId is not Guid selectedFrameId || !AiStudioFrameIdentity.IsSupportedLayout(layout))
                    return Result.Failure<Storyboard>(Error.Conflict("StudioFrameRequired", $"Choose an approved studio keyframe for AI scene {index + 1}."));
                studioFrame = eligible.FirstOrDefault(asset => asset.Id == selectedFrameId);
                var metadata = studioFrame is null ? null : AiStudioFrameIdentity.Read(studioFrame.Regions);
                if (studioFrame is null)
                    return Result.Failure<Storyboard>(Error.Conflict("StudioFrameRequired", $"Choose an approved studio keyframe for AI scene {index + 1}."));

                // An explicitly selected keyframe is a reviewed scene asset. Its original prompt,
                // reference files and output format describe how it was created; they do not make
                // the approved pixels unsafe to send to the video model later.
                studioSource = metadata is null
                    ? source
                    : productSources.FirstOrDefault(asset => asset.Id == metadata.SourceMockupId) ?? source;
            }
            var sceneSource = useStudioFrame ? studioFrame! : source;
            var contextSource = useStudioFrame ? studioSource! : source;
            var productContext = new AiVideoProductContext(product.ProductType, contextSource.Role,
                contextSource.GarmentColor, contextSource.BackgroundColor);
            // P1 is deliberately isolated from every optional creative direction so the team can evaluate
            // the template and its rule set before reintroducing advanced controls.
            var sharedInstructions = AiVideoPromptCompiler.AdvancedControlsEnabled && !usesP1Rules
                ? config.AiGlobalInstructions
                : null;
            IReadOnlyList<VideoPromptExperience>? sceneExperiences = null;
            if (experiences is not null)
                sceneExperiences = experiences.TryGetValue(index, out var retrieved) ? retrieved : [];
            var promptTrace = AiVideoPromptCompiler.CompileTrace(definition, settings, productContext, requested.DurationSeconds,
                sharedInstructions, useStudioFrame, ruleApplication, sceneExperiences);
            var prompt = promptTrace.FinalPrompt;
            var sceneTransition = ResolveSceneTransition(requested, index, config.StandardOptions?.Transition);
            var trimStart = requested.TrimStartFrames ?? 0;
            var trimEnd = requested.TrimEndFrames ?? 0;
            var scene = CreateScene(sceneSource, product.ProductType, config, index, requested.DurationSeconds * 30 - trimStart - trimEnd) with
            {
                GenerationStrategy = "ai_shot",
                Motion = "static",
                Transition = sceneTransition, TransitionFrames = sceneTransition == "none" ? 0 : requested.TransitionDurationFrames ?? TransitionFrames,
                TrimStartFrames = trimStart, TrimEndFrames = trimEnd,
                AiTemplateCode = definition.Code,
                PromptVersion = definition.PromptVersion,
                CompiledPrompt = prompt,
                AiSettings = settings,
                SourceCrop = sourceCrop,
                AiProviderKey = AiVideoModel.ProviderKey,
                AiModelKey = AiVideoModel.ModelKey,
                AiModelName = AiVideoModel.ModelName,
                AiResolution = AiVideoModel.Resolution,
                AiPricePerSecondUsd = AiVideoModel.PricePerSecondUsd,
                AiPromptTrace = promptTrace
            };
            var fingerprint = WorkflowJson.Hash(WorkflowJson.Write(new
            {
                sceneSource.Id,
                sceneSource.MetadataRevision,
                sceneSource.ContentHash,
                SourceCrop = sourceCrop,
                definition.Code,
                definition.PromptVersion,
                // Timeline trims and boundary transitions alter the final composition only; they must not spend for a new AI clip.
                Settings = settings with { DisplayDurationFrames = null, TrimStartFrames = null, TrimEndFrames = null, TransitionIn = null, TransitionDurationFrames = null },
                prompt,
                Provider = AiVideoModel.ProviderKey,
                ModelKey = AiVideoModel.ModelKey,
                Model = AiVideoModel.ModelName,
                Resolution = AiVideoModel.Resolution,
                PricePerSecondUsd = AiVideoModel.PricePerSecondUsd,
                Duration = requested.DurationSeconds,
                Aspect = VideoOutputFormats.Resolve(config.OutputFormat).AspectRatio,
                index
            }));
            plannedScenes.Add(scene);
            shots.Add(new(index, sceneSource.Id, sceneSource.ContentHash!, AiVideoModel.ModelName, definition.Code,
                prompt, requested.DurationSeconds, fingerprint, AiVideoModel.ProviderKey, AiVideoModel.ModelKey, definition.PromptVersion,
                sourceCrop, AiVideoModel.Resolution, AiVideoModel.PricePerSecondUsd));
        }

        var outputFrames = plannedScenes.Sum(scene => scene.DurationFrames);
        var duration = Math.Max(1, (int)Math.Ceiling(outputFrames / 30d));
        var overlapFrames = plannedScenes.Select(scene => scene.TransitionFrames).DefaultIfEmpty().Max();
        var output = VideoOutputFormats.Resolve(config.OutputFormat);
        var boardMode = shots.Count > 0 ? "ai_shot" : "standard";
        var fingerprintAll = WorkflowJson.Hash(WorkflowJson.Write(new
        {
            Product = product.Id,
            config.OutputFormat,
            Mode = boardMode,
            PlannedScenes = plannedScenes,
            Shots = shots
        }));
        return Result.Success(new Storyboard("product_showcase", 2, duration, product.ProductType, plannedScenes,
            fingerprintAll, output.Format, output.Width, output.Height, output.AspectRatio, boardMode, overlapFrames, shots, outputFrames, config.VideoEdit));
    }

    private static string NormalizeTransition(string transition) => transition == "cut" ? "none" : transition;
    private static string ResolveSceneTransition(AiVideoSceneOptions scene, int index, string? fallback) =>
        index == 0 ? "none" : NormalizeTransition(scene.TransitionIn ?? fallback ?? "none");
    private static bool ValidTimelineEdit(AiVideoSceneOptions scene, int index)
    {
        var sourceFrames = scene.Mode == "standard" ? scene.DisplayDurationFrames ?? scene.DurationSeconds * 30
            : scene.DurationSeconds * 30;
        var display = sourceFrames - (scene.TrimStartFrames ?? 0) - (scene.TrimEndFrames ?? 0);
        return display is >= 30 and <= 900 && (scene.TrimStartFrames ?? 0) >= 0 && (scene.TrimEndFrames ?? 0) >= 0 &&
            (scene.TransitionDurationFrames ?? TransitionFrames) is >= 0 and <= 30 &&
            (scene.TransitionIn == null || new[] { "none", "fade", "swipe", "zoom", "flash", "whip_pan", "blur", "cut" }.Contains(scene.TransitionIn));
    }
    private static ScenePlan TrimStandardScene(ScenePlan scene, int sourceFrames, int trimStartFrames, int trimEndFrames)
    {
        var outputFrames = sourceFrames - trimStartFrames - trimEndFrames;
        var denominator = Math.Max(1, sourceFrames - 1);
        var startProgress = Math.Clamp((double)trimStartFrames / denominator, 0, 1);
        var endProgress = Math.Clamp((double)(sourceFrames - trimEndFrames - 1) / denominator, 0, 1);
        return scene with
        {
            DurationFrames = outputFrames,
            Crop = Mix(scene.Crop, scene.EndCrop, startProgress),
            EndCrop = Mix(scene.Crop, scene.EndCrop, endProgress),
            StartScale = Mix(scene.StartScale, scene.EndScale, startProgress),
            EndScale = Mix(scene.StartScale, scene.EndScale, endProgress),
            StartRotation = Mix(scene.StartRotation, scene.EndRotation, startProgress),
            EndRotation = Mix(scene.StartRotation, scene.EndRotation, endProgress),
            TrimStartFrames = trimStartFrames,
            TrimEndFrames = trimEndFrames,
        };
    }
    private static Region Mix(Region start, Region end, double progress) => new(
        Mix(start.X, end.X, progress),
        Mix(start.Y, end.Y, progress),
        Mix(start.Width, end.Width, progress),
        Mix(start.Height, end.Height, progress));
    private static double Mix(double start, double end, double progress) => start + (end - start) * progress;
    private static bool ValidVideoEdit(VideoEditConfig? edit)
    {
        if (edit == null) return true;
        if (edit.Version != 1 || edit.Music is { StartFrame: < 0 or > 900 } || edit.Overlays is { Count: > 12 }) return false;
        return (edit.Overlays ?? []).All(overlay => overlay.Type is "headline" or "cta" && overlay.Text.Length is > 0 and <= 120 &&
            (overlay.Type != "cta" || overlay.Text.Length <= 40) && overlay.EndFrame - overlay.StartFrame >= 15 &&
            overlay.StartFrame >= 0 && overlay.EndFrame <= 900 && overlay.X is >= -1 and <= 2 && overlay.Y is >= -1 and <= 2 && overlay.FontScale is >= .02 and <= .2);
    }

    private static bool SettingsAreSupported(AiVideoTemplateDefinition definition, AiVideoSceneOptions settings) =>
        definition.AllowedSettings.TryGetValue("framing", out var framing) && framing.Contains(settings.Framing, StringComparer.Ordinal) &&
        definition.AllowedSettings.TryGetValue("cameraAngle", out var angles) && angles.Contains(settings.CameraAngle, StringComparer.Ordinal) &&
        definition.AllowedSettings.TryGetValue("cameraMovement", out var movements) && movements.Contains(settings.CameraMovement, StringComparer.Ordinal) &&
        definition.AllowedSettings.TryGetValue("subjectMotion", out var subjectMotion) && subjectMotion.Contains(settings.SubjectMotion, StringComparer.Ordinal) &&
        definition.AllowedSettings.TryGetValue("motionIntensity", out var intensities) && intensities.Contains(settings.MotionIntensity, StringComparer.Ordinal) &&
        definition.AllowedSettings.TryGetValue("lighting", out var lighting) && lighting.Contains(settings.Lighting, StringComparer.Ordinal);

    private static bool HasP1Brief(AiVideoSceneOptions scene) =>
        ValidP1Text(scene.StartingState) && ValidP1Text(scene.PrimaryAction) && ValidP1Text(scene.EndingState);

    private static bool ValidP1Text(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 240 && value.All(character => !char.IsControl(character));

    private Result<Storyboard> PlanAiShot(Storyboard standard, IReadOnlyList<MockupImage> eligible, GenerateVideoConfig config)
    {
        var options = config.AiShotOptions;
        if (options == null) return Result.Failure<Storyboard>(Error.Validation("Choose AI Shot settings."));
        var source = options.MockupId.HasValue ? eligible.FirstOrDefault(x => x.Id == options.MockupId.Value) : null;
        if (options.MockupId.HasValue && source == null)
            return Result.Failure<Storyboard>(Error.Validation("The selected AI Shot source is not an approved mockup."));
        source ??= options.Concept == "material_detail" ? eligible.FirstOrDefault(HasMarkedDetail) ?? eligible[0] : eligible[0];
        if (source == null || !eligible.Any(x => x.Id == source.Id))
            return Result.Failure<Storyboard>(Error.Validation("The selected AI Shot source is not an approved mockup."));
        if (options.Concept is "lifestyle_motion" or "product_in_use" && !options.RightsConfirmed)
            return Result.Failure<Storyboard>(Error.Validation("Lifestyle AI Shots need image-rights confirmation for the selected image."));
        var model = AiVideoModel.ModelName;
        var aspect = config.OutputFormat == "landscape" ? "16:9" : "9:16";
        var prompt = BuildPrompt(options.Concept, options.Prompt);
        AiShotPlan Shot(int index, MockupImage asset, int seconds)
        {
            var direction = options.Coverage == "full_video" && config.DurationSeconds == 15
                ? index == 0 ? " Opening shot with a slow reveal." : " Closing shot from a complementary camera direction." : "";
            var shotPrompt = prompt + direction;
            var hash = WorkflowJson.Hash(WorkflowJson.Write(new
            {
                asset.Id,
                asset.ContentHash,
                asset.MetadataRevision,
                asset.Role,
                Provider = AiVideoModel.ProviderKey,
                ModelKey = AiVideoModel.ModelKey,
                Model = model,
                Resolution = AiVideoModel.Resolution,
                PricePerSecondUsd = AiVideoModel.PricePerSecondUsd,
                options.Concept,
                shotPrompt,
                seconds,
                aspect,
                index
            }));
            return new(index, asset.Id, asset.ContentHash!, model, options.Concept, shotPrompt, seconds, hash,
                AiVideoModel.ProviderKey, AiVideoModel.ModelKey, 1, null, AiVideoModel.Resolution, AiVideoModel.PricePerSecondUsd);
        }
        if (options.Coverage == "single_scene")
        {
            if (options.SceneIndex >= standard.Scenes.Count)
                return Result.Failure<Storyboard>(Error.Validation("Choose an existing storyboard scene for the AI Shot."));
            var stillCount = standard.Scenes.Count - 1;
            if (stillCount < 1) return Result.Failure<Storyboard>(Error.Validation("A mixed video needs at least one Standard scene."));
            var singleSceneTransition = NormalizeTransition(config.StandardOptions?.Transition ?? "none");
            var singleSceneOverlapFrames = standard.Scenes.Count > 1 && singleSceneTransition != "none" ? TransitionFrames : 0;
            var singleSceneOutputFrames = standard.DurationSeconds * 30;
            var remaining = singleSceneOutputFrames - 180;
            var stillPosition = 0;
            var scenes = standard.Scenes.Select((scene, index) => index == options.SceneIndex
                ? CreateScene(source, standard.ProductType, config, index, 180) with
                {
                    GenerationStrategy = "ai_shot", Motion = "static", Transition = index == 0 ? "none" : singleSceneTransition,
                    AiProviderKey = AiVideoModel.ProviderKey, AiModelKey = AiVideoModel.ModelKey, AiModelName = AiVideoModel.ModelName,
                    AiResolution = AiVideoModel.Resolution, AiPricePerSecondUsd = AiVideoModel.PricePerSecondUsd
                }
                : scene with { DurationFrames = remaining / stillCount + (stillPosition++ < remaining % stillCount ? 1 : 0) }).ToArray();
            var shots = new[] { Shot(options.SceneIndex, source, 6) };
            return Result.Success(standard with { Scenes = scenes, Mode = "ai_shot", AiShots = shots,
                TransitionOverlapFrames = singleSceneOverlapFrames, OutputDurationFrames = singleSceneOutputFrames,
                Fingerprint = WorkflowJson.Hash(WorkflowJson.Write(new { standard.Fingerprint, scenes, shots })) });
        }
        if (options.Coverage != "full_video" || config.DurationSeconds is not (8 or 15))
            return Result.Failure<Storyboard>(Error.Validation("Full AI videos must be 8 or 15 seconds."));
        var count = config.DurationSeconds == 8 ? 1 : 2;
        var allSources = new List<MockupImage>();
        for (var index = 0; index < count; index++)
        {
            var requested = options.FullSceneMockupIds is { Count: > 0 } ids && index < ids.Count
                ? eligible.FirstOrDefault(x => x.Id == ids[index]) : null;
            if (options.FullSceneMockupIds is { Count: > 0 } supplied && index < supplied.Count && requested == null)
                return Result.Failure<Storyboard>(Error.Validation("An AI scene uses an unavailable mockup."));
            var chosen = requested ?? source;
            allSources.Add(chosen);
        }
        var transition = NormalizeTransition(config.StandardOptions?.Transition ?? "none");
        var overlapFrames = count > 1 && transition != "none" ? TransitionFrames : 0;
        var outputFrames = count * 240;
        var fullScenes = allSources.Select((asset, index) => CreateScene(asset, standard.ProductType, config, index, 240) with
        { GenerationStrategy = "ai_shot", Motion = "static", Transition = index == 0 ? "none" : transition,
            TransitionFrames = index == 0 || transition == "none" ? 0 : TransitionFrames,
            AiProviderKey = AiVideoModel.ProviderKey, AiModelKey = AiVideoModel.ModelKey, AiModelName = AiVideoModel.ModelName,
            AiResolution = AiVideoModel.Resolution, AiPricePerSecondUsd = AiVideoModel.PricePerSecondUsd }).ToArray();
        var fullShots = allSources.Select((asset, index) => Shot(index, asset, 8)).ToArray();
        return Result.Success(standard with { Scenes = fullScenes, Mode = "ai_shot", AiShots = fullShots,
            TransitionOverlapFrames = overlapFrames, OutputDurationFrames = outputFrames,
            Fingerprint = WorkflowJson.Hash(WorkflowJson.Write(new { standard.Fingerprint, fullScenes, fullShots })) });
    }

    private static string BuildPrompt(string concept, string? instruction)
    {
        var motion = concept switch
        {
            "material_detail" => "A smooth macro camera glide across the visible product material and printed artwork.",
            "lifestyle_motion" => "The adult person already visible in the image moves naturally and subtly while wearing the same product.",
            "product_in_use" => "The adult person already visible in the image naturally uses the same product, with gentle camera motion.",
            _ => "A cinematic, smooth, subtle camera dolly and gentle angle reveal of the product."
        };
        return $"{motion} Start from the supplied image. Keep the exact visible product, colors, print, text and composition unchanged. " +
            "Do not invent hidden product details, new logos or extra people. " +
            // The clip is muted afterwards, but Veo still writes a soundtrack and drops the whole clip if its audio filter objects to it.
            "No dialogue, speech, singing or music: the only sound is quiet ambient room tone. " +
            (string.IsNullOrWhiteSpace(instruction) ? "" : instruction.Trim());
    }

    public ScenePlan CreateScene(MockupImage asset, string productType, GenerateVideoConfig config, int index, int frames,
        StandardSceneOptions? customFraming = null)
    {
        var regions = WorkflowJson.Read<MockupRegions>(asset.Regions);
        var focus = regions.FocalPoint ?? new Point();
        var protectedRegion = asset.Role == "ArtworkDetail" ? regions.Detail ?? regions.Artwork ?? regions.Product : regions.Product;
        var warnings = new List<string>();
        var templateVersion = config.TemplateVersion ?? 1;
        var output = VideoOutputFormats.Resolve(config.OutputFormat);
        var full = new Region(0, 0, 1, 1);
        var crop = full;
        var end = full;
        var startScale = 1d;
        var endScale = 1d;
        var startRotation = 0d;
        var endRotation = 0d;
        var motion = config.SceneMotionPresets is { } presets && index < presets.Count && presets[index] is { } selected
            ? selected : config.StandardOptions?.MotionPreset ?? "zoom";
        if (motion == "varied") motion = new[] { "zoom", "pan_left", "diagonal_up_right", "zoom_out" }[index % 4];
        var sourceRatio = (double)asset.MockupWidthPx / Math.Max(1, asset.MockupHeightPx);
        var outputRatio = (double)output.Width / output.Height;
        var height = sourceRatio < outputRatio ? sourceRatio / outputRatio : 1;
        var width = sourceRatio > outputRatio ? outputRatio / sourceRatio : 1;
        var candidate = new Region(Math.Clamp(focus.X - width / 2, 0, 1 - width), Math.Clamp(focus.Y - height / 2, 0, 1 - height), width, height);
        if (customFraming is not null)
        {
            crop = customFraming.StartCrop ?? candidate;
            end = customFraming.EndCrop ?? crop;
            startScale = customFraming.StartScale;
            endScale = customFraming.EndScale;
            startRotation = customFraming.StartRotation;
            endRotation = customFraming.EndRotation;
            motion = "custom";
        }
        else if (motion == "contain_gentle")
        {
            crop = full;
            end = full;
        }
        // Keep the server plan consistent with the editor: assets without an
        // annotated product region have no bounds to preserve, so the selected
        // camera movement must not be silently replaced by contain_gentle.
        else if (protectedRegion is not null && !Contains(candidate, protectedRegion))
        {
            motion = templateVersion >= 2 && motion != "static" ? "contain_gentle" : "static";
            warnings.Add(templateVersion >= 2 && motion == "contain_gentle"
                ? "Vertical crop would clip the protected region. Gentle full-image motion is used."
                : "Vertical crop would clip the protected region. Static contain framing is used.");
        }
        else
        {
            crop = candidate;
            end = crop;
            if (motion != "static")
            {
                (crop, end) = motion switch
                {
                    "pan" => Pan(candidate, 1, 0),
                    "pan_left" => Pan(candidate, -1, 0),
                    "pan_up" => Pan(candidate, 0, -1),
                    "pan_down" => Pan(candidate, 0, 1),
                    "diagonal_up_right" => Pan(candidate, 1, -1),
                    "diagonal_up_left" => Pan(candidate, -1, -1),
                    "diagonal_down_right" => Pan(candidate, 1, 1),
                    "diagonal_down_left" => Pan(candidate, -1, 1),
                    _ => (candidate, candidate)
                };
                if (protectedRegion is not null &&
                    (!Contains(crop, protectedRegion) || !Contains(end, protectedRegion)))
                {
                    crop = full; end = full; motion = templateVersion >= 2 ? "contain_gentle" : "static";
                    warnings.Add("Motion reduced to preserve product/artwork bounds.");
                }
                else if (motion is "gentle" or "zoom")
                {
                    // Transform scale advances at a constant rate, unlike a linearly
                    // interpolated crop whose visible zoom accelerates near the end.
                    endScale = PushPullScale;
                }
                else if (motion == "zoom_out")
                {
                    startScale = PushPullScale;
                }
                else if (motion is "rotate_cw" or "rotate_zoom_cw")
                {
                    startScale = RotationCoverScale(output);
                    endScale = startScale * (motion == "rotate_zoom_cw" ? PushPullScale : 1d);
                    endRotation = 12;
                }
                else if (motion is "rotate_ccw" or "rotate_zoom_ccw")
                {
                    startScale = RotationCoverScale(output);
                    endScale = startScale * (motion == "rotate_zoom_ccw" ? PushPullScale : 1d);
                    endRotation = -12;
                }
            }
        }
        if (asset.MockupWidthPx < output.Width / 2 || asset.MockupHeightPx < output.Height / 2)
            warnings.Add($"Low-resolution source may appear soft at {output.Width} × {output.Height}.");
        return new(asset.Id, asset.MetadataRevision, asset.ContentHash!, asset.Role, index, frames, crop, end, motion,
            config.StandardOptions?.Transition ?? "none", config.TextOverlay, "standard", warnings,
            StartScale: startScale, EndScale: endScale, StartRotation: startRotation, EndRotation: endRotation);
    }

    private static string ResolveTemplate(string requested, IReadOnlyList<MockupImage> eligible, int templateVersion)
    {
        if (requested != "auto") return requested;
        if (eligible.Select(x => x.VariantKey).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Count() > 1)
            return "variant_showcase";
        if (eligible.Any(x => x.Role == "ArtworkDetail" || templateVersion >= 2 && WorkflowJson.Read<MockupRegions>(x.Regions).Detail != null))
            return "design_detail";
        return "product_showcase";
    }
    private static bool HasDetail(MockupImage asset) => asset.Role == "ArtworkDetail" ||
        WorkflowJson.Read<MockupRegions>(asset.Regions) is { Detail: not null } or { Artwork: not null };
    private static bool HasMarkedDetail(MockupImage asset) =>
        WorkflowJson.Read<MockupRegions>(asset.Regions) is { Detail: not null } or { Artwork: not null };
    private static (Region Start, Region End) Pan(Region bounds, int horizontal, int vertical)
    {
        var width = bounds.Width * (1d / 1.2d);
        var height = bounds.Height * (1d / 1.2d);
        var horizontalTravel = bounds.Width - width;
        var verticalTravel = bounds.Height - height;
        var startX = bounds.X + (horizontal > 0 ? 0 : horizontal < 0 ? horizontalTravel : horizontalTravel / 2);
        var startY = bounds.Y + (vertical > 0 ? 0 : vertical < 0 ? verticalTravel : verticalTravel / 2);
        var endX = bounds.X + (horizontal > 0 ? horizontalTravel : horizontal < 0 ? 0 : horizontalTravel / 2);
        var endY = bounds.Y + (vertical > 0 ? verticalTravel : vertical < 0 ? 0 : verticalTravel / 2);
        return (new(startX, startY, width, height), new(endX, endY, width, height));
    }

    private static double RotationCoverScale(VideoOutputSpec output)
    {
        const double rotationRadians = 12d * Math.PI / 180d;
        var aspect = Math.Max(.01d, (double)output.Width / Math.Max(1, output.Height));
        var cover = Math.Max(Math.Cos(rotationRadians) + Math.Sin(rotationRadians) / aspect,
            Math.Cos(rotationRadians) + Math.Sin(rotationRadians) * aspect);
        return Math.Min(5d, Math.Round(cover * 1.01d, 3));
    }
    private static bool Contains(Region outer, Region inner) => inner.X >= outer.X && inner.Y >= outer.Y &&
        inner.X + inner.Width <= outer.X + outer.Width + 1e-9 && inner.Y + inner.Height <= outer.Y + outer.Height + 1e-9;
    private static bool ValidRegion(Region? region) => region is null || double.IsFinite(region.X + region.Y + region.Width + region.Height) &&
        region.X is >= -1 and <= 1 && region.Y is >= -1 and <= 1 && region.Width > 0 && region.Height > 0 && region.Width <= 1 && region.Height <= 1;
    private static bool ValidScale(double? scale) => scale is null || double.IsFinite(scale.Value) && scale is >= .01 and <= 5;
    private static bool ValidScale(double scale) => double.IsFinite(scale) && scale is >= .01 and <= 5;
    private static bool ValidRotation(double? rotation) => rotation is null || double.IsFinite(rotation.Value) && rotation is >= -9999 and <= 9999;
    private static bool ValidRotation(double rotation) => double.IsFinite(rotation) && rotation is >= -9999 and <= 9999;
}
