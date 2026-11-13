using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>How one scene found its image for one design.</summary>
public sealed record SceneResolution(int SceneIndex, Guid? MockupId, string? Warning, bool Overridden, bool Skipped);

/// <summary>A video configuration rewritten to name one design's own mock-ups.</summary>
/// <param name="Error">Set when the design cannot be rendered from this configuration.</param>
public sealed record ResolvedVideoConfig(GenerateVideoConfig Config, IReadOnlyList<SceneResolution> Scenes, IReadOnlyList<string> Warnings, string? Error);

/// <summary>
/// Turns the scene list a seller set up once into the scene list of one design.
/// </summary>
/// <remarks>
/// The scene editor picks images of one product, the preview product. Every mock-up composited from a template
/// remembers the template and the garment color, and every design of the batch is placed on the same templates, so
/// a scene whose image came from template T in color C uses, for another design, that design's mock-up from T in C.
/// An image that came from no template (an uploaded photo, a lifestyle shot) maps by its role when that role is not
/// the default one. A scene may also say where its image comes from (<see cref="SceneSource"/>), and the seller may
/// pick a design's image for a scene outright (<see cref="TargetOverride"/>). Whatever is still missing follows the
/// configuration's <see cref="GenerateVideoConfig.MissingSourcePolicy"/>.
/// </remarks>
public static class SceneSourceResolver
{
    public const string HeroPolicy = "hero";
    public const string SkipPolicy = "skip";

    /// <param name="approved">The design's mock-ups approved at their current revision.</param>
    /// <param name="referenced">Mock-ups the configuration names, of any product, used to translate them by template.</param>
    public static ResolvedVideoConfig Resolve(GenerateVideoConfig config, IReadOnlyList<MockupImage> approved,
        IReadOnlyDictionary<Guid, MockupImage> referenced, TargetOverride? overrides = null)
    {
        var usable = approved.Where(asset => AiStudioFrameIdentity.Read(asset.Regions) is null).ToArray();
        var approvedIds = approved.Select(asset => asset.Id).ToHashSet();
        var warnings = new List<string>();
        var resolved = config with
        {
            SelectedMockupIds = TranslateAll(config.SelectedMockupIds, usable, approvedIds, referenced),
            SceneOrder = TranslateAll(config.SceneOrder, usable, approvedIds, referenced),
            StandardScenes = config.StandardScenes?
                .Select(scene => Translate(scene.MockupId, usable, approvedIds, referenced) is { } id ? scene with { MockupId = id } : null)
                .OfType<StandardSceneOptions>().DistinctBy(scene => scene.MockupId).ToArray(),
            SharedMockupId = config.SharedMockupId is { } shared ? Translate(shared, usable, approvedIds, referenced) : null,
            AiStudioSourceMockupId = config.AiStudioSourceMockupId is { } source ? Translate(source, usable, approvedIds, referenced) : null,
            AiStudioFrameId = config.AiStudioFrameId is { } frame && approvedIds.Contains(frame) ? frame : null,
            // The legacy single-shot settings may name the clip's source: this design's counterpart, else its first
            // approved mock-up (the planner's default when none is named).
            AiShotOptions = config.AiShotOptions is { MockupId: Guid shot } options
                ? options with { MockupId = Translate(shot, usable, approvedIds, referenced) }
                : config.AiShotOptions,
        };
        if (resolved.AssetSelection == "manual" && resolved.SelectedMockupIds is not { Count: > 0 })
            resolved = resolved with { AssetSelection = "automatic", SelectedMockupIds = null };
        if (resolved.SceneOrder is { Count: 0 }) resolved = resolved with { SceneOrder = null };
        if (resolved.StandardScenes is { Count: 0 }) resolved = resolved with { StandardScenes = null };

        // A workflow saved without a scene list has the scene the editor shows for it: one scene on the main mock-up.
        var authored = config.AiScenes is { Count: > 0 };
        var scenes = authored ? config.AiScenes! : DefaultScenes(config);
        if (scenes is null)
        {
            if (usable.Length == 0) return new(resolved, [], warnings, "This design has no approved mock-up.");
            return new(resolved, [], warnings, null);
        }

        // The scene made here has no image of its own to be missing: it always starts from the main mock-up.
        var skipMissing = authored && string.Equals(config.MissingSourcePolicy, SkipPolicy, StringComparison.Ordinal);
        var hero = Hero(usable);
        var results = new List<SceneResolution>(scenes.Count);
        var outputScenes = new List<AiVideoSceneOptions>(scenes.Count);
        var motions = new List<string?>(scenes.Count);
        for (var index = 0; index < scenes.Count; index++)
        {
            var scene = scenes[index];
            Guid? chosen = null;
            string? warning = null;
            var overridden = false;
            if (overrides?.Scenes.FirstOrDefault(item => item.SceneIndex == index) is { } picked)
            {
                if (approvedIds.Contains(picked.MockupId)) { chosen = picked.MockupId; overridden = true; }
                else warning = $"Scene {index + 1}: the image picked for this design is no longer approved.";
            }
            if (chosen is null && scene.Source is { } described)
                chosen = FromSource(described, index, usable, results, ref warning);
            if (chosen is null && scene.Source is null && scene.MockupId is Guid named && named != Guid.Empty)
                chosen = Translate(named, usable, approvedIds, referenced);
            // An AI scene that shows a person starts from this design's own model photo: a flat mock-up has nobody to
            // film, and the clip is paid for, so a design without one stops the run instead of rendering the wrong shot.
            if (!overridden && scene.Mode != "standard" && AiVideoPromptCompiler.RequiresPerson(scene.TemplateCode))
            {
                var person = PersonImage(chosen, usable);
                if (person is null)
                    return new(resolved, results, warnings,
                        $"Scene {index + 1} shows a person, and this design has no approved model photo. Approve the one made for it at Mockup Approval, or make one from another model there.");
                if (chosen is not null && person != chosen)
                    warning ??= $"Scene {index + 1}: this design's model photo is used, since the scene shows a person.";
                chosen = person;
            }
            if (chosen is null)
            {
                if (skipMissing)
                {
                    warning ??= $"Scene {index + 1}: this design has no matching image, so the scene is left out.";
                    results.Add(new(index, null, warning, overridden, true));
                    warnings.Add(warning);
                    continue;
                }
                if (hero is null)
                {
                    results.Add(new(index, null, warning, overridden, true));
                    return new(resolved, results, warnings, "This design has no approved mock-up.");
                }
                chosen = hero.Id;
                // A scene with no start frame chosen is the normal case, not a mismatch worth a warning.
                if (authored)
                    warning ??= scene.MockupId is Guid unnamed && unnamed != Guid.Empty || scene.Source is not null
                        ? $"Scene {index + 1}: this design has no matching image, so it uses the main mock-up."
                        : null;
            }

            var output = scene with { MockupId = chosen };
            // A keyframe made for another product cannot start this design's scene: start from the mock-up itself.
            if (output.StartFrameMode == "studio" && (output.StudioFrameId is not Guid frameId || !approvedIds.Contains(frameId)))
            {
                output = output with { StartFrameMode = "direct", StudioFrameId = null, StudioPrompt = null, StudioReferenceHashes = null };
                warning ??= $"Scene {index + 1}: its keyframe belongs to another design, so the scene starts from the mock-up.";
            }
            if (warning is not null) warnings.Add(warning);
            results.Add(new(index, chosen, warning, overridden, false));
            outputScenes.Add(output);
            motions.Add(config.SceneMotionPresets is { } presets && index < presets.Count ? presets[index] : null);
        }

        if (outputScenes.Count == 0)
            return new(resolved, results, warnings, "No scene has an image for this design.");
        var seconds = outputScenes.Sum(scene => scene.DurationSeconds);
        if (outputScenes.Count != scenes.Count && seconds < VideoDurationPolicy.MinimumVideoSeconds)
            return new(resolved, results, warnings, $"The scenes left for this design last {seconds} s; a video needs at least {VideoDurationPolicy.MinimumVideoSeconds} s.");
        resolved = resolved with
        {
            AiScenes = outputScenes,
            DurationSeconds = outputScenes.Count == scenes.Count ? resolved.DurationSeconds : seconds,
            SceneMotionPresets = config.SceneMotionPresets is null ? null : motions,
        };
        return new(resolved, results, warnings, null);
    }

    /// <summary>
    /// The scene list of a Standard workflow saved without one: the Generate Video editor opens such a workflow with a
    /// single scene and no start frame, so each design gets that scene, as long as the video. A workflow that names its
    /// own images or order keeps them (null here), and so does manual selection.
    /// </summary>
    private static IReadOnlyList<AiVideoSceneOptions>? DefaultScenes(GenerateVideoConfig config) =>
        config.Mode == "standard" && config.AssetSelection != "manual" && config.SceneOrder is not { Count: > 0 } &&
        config.StandardScenes is not { Count: > 0 } && config.SelectedMockupIds is not { Count: > 0 }
            ? [new AiVideoSceneOptions(
                DurationSeconds: Math.Clamp(config.DurationSeconds, VideoDurationPolicy.MinimumVideoSeconds, VideoDurationPolicy.MaximumVideoSeconds),
                Mode: "standard", StartFrameMode: "direct", StudioLayout: "none")]
            : null;

    /// <summary>
    /// The image a scene with a person starts from: the one already chosen when it is a model photo, else the
    /// design's own model photo (its Lifestyle mock-up), or null when it has none.
    /// </summary>
    private static Guid? PersonImage(Guid? chosen, IReadOnlyList<MockupImage> usable) =>
        chosen is { } picked && usable.Any(asset => asset.Id == picked && asset.Role == ModelPhotoMockupService.Role)
            ? picked
            : Ordered(usable.Where(asset => asset.Role == ModelPhotoMockupService.Role)).FirstOrDefault()?.Id;

    /// <summary>The design's main mock-up: a Hero made from a template first, then the oldest.</summary>
    public static MockupImage? Hero(IReadOnlyList<MockupImage> usable) => usable
        .OrderBy(asset => asset.Role == "Hero" ? 0 : 1)
        .ThenBy(asset => asset.MockupTemplateId is null ? 1 : 0)
        .ThenBy(asset => asset.VariantKey, StringComparer.Ordinal)
        .ThenBy(asset => asset.CreatedAt)
        .ThenBy(asset => asset.Id)
        .FirstOrDefault();

    private static Guid? FromSource(SceneSource source, int index, IReadOnlyList<MockupImage> usable,
        IReadOnlyList<SceneResolution> earlier, ref string? warning)
    {
        switch (source.Kind)
        {
            case "template" when source.TemplateId is Guid templateId:
                var made = usable.Where(asset => asset.MockupTemplateId == templateId).ToArray();
                if (made.Length == 0) return null;
                if (!string.IsNullOrEmpty(source.VariantKey))
                {
                    var exact = made.FirstOrDefault(asset => string.Equals(asset.VariantKey, source.VariantKey, StringComparison.OrdinalIgnoreCase));
                    if (exact is not null) return exact.Id;
                    warning = $"Scene {index + 1}: this design has no {source.VariantKey} mock-up from the template, so another color is used.";
                }
                return Ordered(made).First().Id;
            case "role" when !string.IsNullOrEmpty(source.Role):
                return Ordered(usable.Where(asset => asset.Role == source.Role)).FirstOrDefault()?.Id;
            case "same_as" when source.RefScene is int reference && reference >= 0 && reference < index:
                return earlier.FirstOrDefault(item => item.SceneIndex == reference)?.MockupId;
            default:
                return null;
        }
    }

    /// <summary>The design's counterpart of a mock-up: itself, the same template and color, or the same non-default role.</summary>
    private static Guid? Translate(Guid id, IReadOnlyList<MockupImage> usable, IReadOnlySet<Guid> approvedIds,
        IReadOnlyDictionary<Guid, MockupImage> referenced)
    {
        if (approvedIds.Contains(id)) return id;
        if (!referenced.TryGetValue(id, out var original)) return null;
        if (original.MockupTemplateId is Guid templateId)
        {
            var made = usable.Where(asset => asset.MockupTemplateId == templateId).ToArray();
            if (made.Length > 0)
                return (made.FirstOrDefault(asset => string.Equals(asset.VariantKey, original.VariantKey, StringComparison.OrdinalIgnoreCase))
                    ?? Ordered(made).First()).Id;
        }
        if (!string.IsNullOrEmpty(original.Role) && original.Role != "Hero")
            return Ordered(usable.Where(asset => asset.Role == original.Role)).FirstOrDefault()?.Id;
        return null;
    }

    private static IReadOnlyList<Guid>? TranslateAll(IReadOnlyList<Guid>? ids, IReadOnlyList<MockupImage> usable,
        IReadOnlySet<Guid> approvedIds, IReadOnlyDictionary<Guid, MockupImage> referenced) =>
        ids?.Select(id => Translate(id, usable, approvedIds, referenced)).OfType<Guid>().Distinct().ToArray();

    private static IEnumerable<MockupImage> Ordered(IEnumerable<MockupImage> assets) => assets
        .OrderBy(asset => asset.VariantKey, StringComparer.Ordinal)
        .ThenBy(asset => asset.CreatedAt)
        .ThenBy(asset => asset.Id);
}
