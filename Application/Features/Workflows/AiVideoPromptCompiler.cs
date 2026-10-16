using System.Text.Json;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

/// <summary>Persistable prompt and control schema for one AI video scene template.</summary>
public sealed record AiVideoTemplateDefinition(
    string Code,
    string Name,
    string Description,
    int PromptVersion,
    string BasePrompt,
    bool RequiresLifestyle,
    bool RequiresDetail,
    IReadOnlyDictionary<string, string[]> AllowedSettings,
    AiVideoSceneOptions Defaults,
    string? CommonPrompt = null);

/// <summary>Structured product and approved-source metadata used only while compiling AI Scene prompts.</summary>
internal sealed record AiVideoProductContext(
    string ProductType,
    string SourceRole,
    string? GarmentColor = null,
    string? BackgroundColor = null);

/// <summary>Builds a provider prompt from a versioned template and validated scene settings.</summary>
public static class AiVideoPromptCompiler
{
    public const string CommonGuardrail = "Product identity constraint for the entire shot: use the approved source image as the authoritative visual reference and keep the same exact product in every frame. Preserve its visible construction, silhouette, proportions, main colors, material character, distinctive details, and the placement, scale, geometry, colors, spelling and lettering of every visible artwork or logo. Do not redraw, replace, duplicate, morph or reinterpret the product or its printed design. Natural perspective, fabric folds and physically plausible material movement are allowed only when required by the selected action; they must not change the design content or detach it from the product surface. Create one continuous shot without cuts, dissolves, ghosted duplicates or sudden reframing. Do not introduce unrelated text, watermarks or extra products. No dialogue, speech, singing or music; use quiet ambient room tone only. These identity and continuity constraints take precedence over any earlier direction that conflicts with them.";

    private static readonly IReadOnlyDictionary<string, AiVideoTemplateDefinition> BuiltIns = CreateBuiltIns();

    public static IReadOnlyList<AiVideoTemplateDefinition> Catalog(IEnumerable<VideoTemplate>? persisted = null)
    {
        var stored = ReadPersisted(persisted);
        return BuiltIns.Values.Select(item => stored.TryGetValue(item.Code, out var overrideValue) ? overrideValue : item)
            .Select(WithRequiredGuardrail)
            .OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
    }

    public static AiVideoTemplateDefinition? Resolve(string code, IEnumerable<VideoTemplate>? persisted = null)
    {
        var stored = ReadPersisted(persisted);
        var resolved = stored.TryGetValue(code, out var fromDatabase) ? fromDatabase : BuiltIns.GetValueOrDefault(code);
        return resolved is null ? null : WithRequiredGuardrail(resolved);
    }

    internal static string Compile(AiVideoTemplateDefinition template, AiVideoSceneOptions options, AiVideoProductContext productContext, int durationSeconds, string? sharedInstructions = null, bool stableSingleImage = false)
    {
        var settings = ApplyDefaults(template, options);
        var cameraMovement = stableSingleImage && template.Code == "cinematic_product_showcase"
            ? StableStudioCameraMovement(settings.CameraMovement)
            : CameraMovement(settings.CameraMovement);
        var productType = NormalizeProductType(productContext.ProductType);
        var prompt = template.BasePrompt
            .Replace("{{FRAMING}}", Framing(settings.Framing), StringComparison.Ordinal)
            .Replace("{{CAMERA_ANGLE}}", CameraAngle(settings.CameraAngle), StringComparison.Ordinal)
            .Replace("{{CAMERA_MOVEMENT}}", cameraMovement, StringComparison.Ordinal)
            .Replace("{{SUBJECT_MOTION}}", SubjectMotion(settings.SubjectMotion), StringComparison.Ordinal)
            .Replace("{{MOTION_INTENSITY}}", MotionIntensity(settings.MotionIntensity), StringComparison.Ordinal)
            .Replace("{{LIGHTING}}", Lighting(settings.Lighting), StringComparison.Ordinal)
            .Replace("{{PRODUCT_ACTION}}", ProductAction(productType), StringComparison.Ordinal)
            .Replace("{{DURATION}}", durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var productContextPrompt = ProductContextPrompt(productType, productContext);
        var sceneGuidance = SceneGuidance(template, productType, stableSingleImage);
        var personGuidance = template.RequiresLifestyle
            ? "Keep any adult already visible in the source recognizable and let them move naturally. Do not introduce additional people."
            : null;
        var commonPrompt = EffectiveCommonPrompt(template.CommonPrompt);
        var selectedControls = stableSingleImage && template.Code == "cinematic_product_showcase"
            ? $"Selected scene controls: framing {Framing(settings.Framing)}; {CameraAngle(settings.CameraAngle)}; {Lighting(settings.Lighting)}. Keep garment movement minimal."
            : $"Selected scene controls: framing {Framing(settings.Framing)}; {CameraAngle(settings.CameraAngle)}; {cameraMovement}; subject motion {SubjectMotion(settings.SubjectMotion)}; motion intensity {MotionIntensity(settings.MotionIntensity)}; {Lighting(settings.Lighting)}";
        var shared = string.IsNullOrWhiteSpace(sharedInstructions) ? null : $"Shared direction for all scenes: {sharedInstructions.Trim()}";
        var custom = string.IsNullOrWhiteSpace(settings.ExtraInstructions) ? null : settings.ExtraInstructions.Trim();
        var sourceGuardrail = SourceGuardrail(productContext, stableSingleImage);
        var templateGuardrail = TemplateGuardrail(template.Code, settings, stableSingleImage);
        return string.Join("\n\n", new[] { prompt, productContextPrompt, sceneGuidance, personGuidance, selectedControls, shared, custom, commonPrompt, sourceGuardrail, templateGuardrail }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static AiVideoTemplateDefinition WithRequiredGuardrail(AiVideoTemplateDefinition template)
    {
        var defaults = template.Code == "macro_detail_reveal"
            ? template.Defaults with { CameraMovement = "locked", SubjectMotion = "none", MotionIntensity = "subtle" }
            : template.Defaults;
        return template with { CommonPrompt = EffectiveCommonPrompt(template.CommonPrompt), Defaults = defaults };
    }

    private static string EffectiveCommonPrompt(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return CommonGuardrail;
        if (configured.Contains(CommonGuardrail, StringComparison.Ordinal))
            return configured.Trim();
        return $"{configured.Trim()}\n\n{CommonGuardrail}";
    }

    private static string SourceGuardrail(AiVideoProductContext context, bool stableSingleImage)
    {
        var sourceConstraint = context.SourceRole switch
        {
            "Hero" => "Hero-source constraint: preserve the product silhouette, support and visible front-facing design; do not invent a different product side or construction.",
            "Lifestyle" => "Lifestyle-source constraint: preserve the visible person's identity, clothing, product, setting and product-to-body relationship. Natural body motion may change folds and perspective, but must not replace, relocate or redesign the product artwork.",
            "ArtworkDetail" => "Detail-source constraint: treat the supplied image as a close view of artwork physically printed on the product. Preserve every visible letter, line, color boundary and graphic element; do not turn it into a separate poster or floating graphic.",
            "AlternativeAngle" => "Alternative-angle constraint: retain the approved viewpoint and all visible construction details; do not infer a conflicting side of the product.",
            "Variant" => "Variant-source constraint: preserve this exact approved color and design variant throughout the shot.",
            _ => "Source constraint: preserve every product detail that is visible in the approved source and do not invent conflicting unseen construction."
        };
        var keyframeConstraint = stableSingleImage
            ? "Reviewed-keyframe constraint: use the supplied keyframe as the exact opening composition. Begin the selected motion immediately and continuously from that frame without a hold-then-transition, crossfade, dissolve, double exposure or replacement composition. Keep its environment, support, background and light direction coherent unless the selected template explicitly calls for a restrained lighting change."
            : null;
        return string.Join(" ", new[] { sourceConstraint, keyframeConstraint }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string TemplateGuardrail(string templateCode, AiVideoSceneOptions settings, bool stableSingleImage) => templateCode switch
    {
        "cinematic_product_showcase" when stableSingleImage =>
            $"Showcase constraint: keep the reviewed set, background, hanger or surface and light direction stable. {StableStudioCameraGuidance(settings.CameraMovement)} Keep the product attached to its support and almost still, with only barely perceptible natural material and shadow motion. No floating product, invented viewpoint, orbit, camera roll, snap zoom or sudden reframing.",
        "cinematic_product_showcase" =>
            "Showcase constraint: the product remains the stable visual anchor. Camera parallax and lighting may develop gently, but the product silhouette, construction and design must not drift or transform.",
        "macro_detail_reveal" =>
            $"Macro-detail constraint: the supplied close-up is already the opening detail shot; do not create an establishing frame or a second version of the artwork. {MacroCameraGuidance(settings.CameraMovement)} Keep the complete visible printed design in sharp, stable focus; limit shallow depth of field to surrounding material outside the artwork. Preserve every visible letter, spelling, line, character feature, color boundary and print edge exactly throughout. Reveal only real surface texture and subtle light response. No artwork redraw, focus pull across lettering, morph, crossfade, ghosting or transition to a newly composed detail.",
        "lifestyle_in_use" =>
            "Lifestyle constraint: allow only physically plausible body and fabric movement. Keep the same garment or product on the same person, with artwork anchored to the same surface location and changing only through natural folds, occlusion and perspective. Do not change the design when the person turns.",
        "creator_pov_ugc" =>
            "UGC constraint: handheld motion belongs to the camera, not to the product geometry. Keep the same product and artwork stable while it is presented; avoid focus hunting, warped edges, anatomy crossing through the product, or hands covering the main design.",
        "everyday_story_moment" =>
            "Story constraint: natural action may change distance, folds and perspective, but the same product must remain identifiable and attached to the same person or object. Do not swap its color, construction, variant or artwork during the action.",
        "signature_product_reveal" =>
            "Reveal constraint: reveal the existing product only through restrained camera and lighting changes. Do not materialize, assemble, dissolve, morph, recolor or redesign any part of the product. Finish with the same product identity and artwork shown in the approved source.",
        _ =>
            "Template constraint: preserve the approved product identity and visible design continuously while applying only the selected camera, subject and lighting motion."
    };

    private static string MacroCameraGuidance(string value) => value switch
    {
        "locked" => "Keep the camera locked with no zoom, pan, reframing or camera-angle change; only a subtle light sweep may move.",
        "push_in" => "Use one extremely slow optical push-in of no more than a few percent across the full shot, without generating a new viewpoint.",
        "pan_left" => "Use one extremely slow lateral move to the left of no more than a few percent of the frame, without changing scale or viewpoint.",
        "pan_right" => "Use one extremely slow lateral move to the right of no more than a few percent of the frame, without changing scale or viewpoint.",
        _ => "Keep camera motion minimal and continuous without changing viewpoint."
    };

    private static string ProductContextPrompt(string productType, AiVideoProductContext context)
    {
        var sourceRole = context.SourceRole switch
        {
            "Hero" => "hero product view",
            "Lifestyle" => "lifestyle product view",
            "ArtworkDetail" => "artwork or material detail view",
            "AlternativeAngle" => "alternative product angle",
            "Variant" => "product variant view",
            _ => "approved product view"
        };
        var colorHints = new List<string>();
        AddHint(colorHints, "garment color", context.GarmentColor);
        AddHint(colorHints, "background color", context.BackgroundColor);
        var hints = colorHints.Count == 0
            ? string.Empty
            : $"Optional mockup color hints: {string.Join("; ", colorHints)}. Follow the visible image if a hint differs.";
        return string.Join(" ", new[]
        {
            $"Product context: {productType}. The approved {sourceRole} image is the primary reference for the product's visible design, colors and proportions.",
            hints
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static void AddHint(List<string> hints, string label, string? value)
    {
        var cleaned = CleanHint(value);
        if (cleaned.Length > 0) hints.Add($"{label}: {cleaned}");
    }

    private static string CleanHint(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : new string(value.Trim().Where(character => !char.IsControl(character)).Take(80).ToArray());

    private static string SceneGuidance(AiVideoTemplateDefinition template, string productType, bool stableSingleImage)
    {
        var product = productType switch
        {
            "T-shirt" or "hoodie" => "Allow natural fabric movement and soft folds; keep printed artwork visually attached to the fabric as it moves.",
            "mug" => "Treat the mug body and handle as a rigid shape; keep the artwork in a consistent position relative to the mug.",
            "poster" => "Keep the printed poster surface flat and readable; do not make it behave like fabric or bend unnaturally.",
            "tote bag" => "Keep the bag body, straps and artwork moving together naturally, with the print attached to the fabric.",
            "phone case" => "Keep the case shape and visible camera or button cutouts consistent; keep artwork fixed relative to the case.",
            _ => "Keep the visible product construction coherent while allowing natural material motion."
        };
        var templateGuidance = template.Code switch
        {
            "cinematic_product_showcase" when stableSingleImage => "The product remains the main subject; keep its reviewed studio set and background stable throughout the shot.",
            "cinematic_product_showcase" or "signature_product_reveal" => "The product remains the main subject while the background and lighting may develop naturally to create a polished showcase.",
            "macro_detail_reveal" => "Prioritize the selected close-up or marked detail, showing its surface texture and artwork clearly; keep the camera focused on that detail.",
            "lifestyle_in_use" or "creator_pov_ugc" or "everyday_story_moment" => "Keep the product integrated naturally into the existing lifestyle scene and visible during the action.",
            _ => string.Empty
        };
        return string.Join(" ", new[] { product, templateGuidance }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string NormalizeProductType(string productType) => productType.Trim().ToLowerInvariant() switch
    {
        "tshirt" or "t-shirt" or "t_shirt" => "T-shirt",
        "hoodie" => "hoodie",
        "mug" => "mug",
        "poster" => "poster",
        "tote_bag" or "tote bag" => "tote bag",
        "phone_case" or "phone case" => "phone case",
        _ => "product"
    };

    public static AiVideoSceneOptions ApplyDefaults(AiVideoTemplateDefinition template, AiVideoSceneOptions options)
    {
        string Value(string key, string value, string fallback) =>
            template.AllowedSettings.TryGetValue(key, out var allowed) && allowed.Contains(value, StringComparer.Ordinal)
                ? value
                : fallback;
        var defaults = template.Defaults;
        return options with
        {
            TemplateCode = template.Code,
            Framing = Value("framing", options.Framing, defaults.Framing),
            CameraAngle = Value("cameraAngle", options.CameraAngle, defaults.CameraAngle),
            CameraMovement = Value("cameraMovement", options.CameraMovement, defaults.CameraMovement),
            SubjectMotion = Value("subjectMotion", options.SubjectMotion, defaults.SubjectMotion),
            MotionIntensity = Value("motionIntensity", options.MotionIntensity, defaults.MotionIntensity),
            Lighting = Value("lighting", options.Lighting, defaults.Lighting),
            ExtraInstructions = string.IsNullOrWhiteSpace(options.ExtraInstructions) ? null : options.ExtraInstructions.Trim()
        };
    }

    private static IReadOnlyDictionary<string, AiVideoTemplateDefinition> ReadPersisted(IEnumerable<VideoTemplate>? rows)
    {
        if (rows == null) return new Dictionary<string, AiVideoTemplateDefinition>(StringComparer.Ordinal);
        var result = new Dictionary<string, AiVideoTemplateDefinition>(StringComparer.Ordinal);
        foreach (var row in rows.Where(row => row.IsActive == true && row.Code != null))
        {
            try
            {
                using var document = JsonDocument.Parse(row.EffectsConfig);
                if (!document.RootElement.TryGetProperty("aiVideo", out var definitionElement)) continue;
                var definition = definitionElement.Deserialize<AiVideoTemplateDefinition>(WorkflowJson.Options);
                if (definition is not null && definition.Code == row.Code && definition.PromptVersion > 0 && !string.IsNullOrWhiteSpace(definition.BasePrompt))
                    result[row.Code!] = definition;
            }
            catch (JsonException)
            {
                // An invalid or older catalog row falls back to the last known built-in prompt.
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<string, AiVideoTemplateDefinition> CreateBuiltIns()
    {
        static Dictionary<string, string[]> Settings(string[] framing, string[] angle, string[] movement, string[] subject, string[] lighting) => new(StringComparer.Ordinal)
        {
            ["framing"] = framing,
            ["cameraAngle"] = angle,
            ["cameraMovement"] = movement,
            ["subjectMotion"] = subject,
            ["motionIntensity"] = ["subtle", "balanced"],
            ["lighting"] = lighting
        };

        var showcase = new AiVideoTemplateDefinition("cinematic_product_showcase", "Cinematic Product Showcase",
            "A polished product hero shot with controlled camera movement.", 4,
            "Create a premium e-commerce hero shot of the exact product shown in the approved image supplied for this scene. Keep the product clear and stable while the camera uses {{CAMERA_MOVEMENT}} from {{CAMERA_ANGLE}}, using {{FRAMING}}. {{LIGHTING}} Use realistic materials, clean commercial composition and natural shadows. Product motion is {{SUBJECT_MOTION}}. Create a {{DURATION}}-second shot.",
            false, false, Settings(["wide", "medium", "medium_close", "close_up"], ["eye_level", "three_quarter", "low_angle", "top_down"], ["locked", "push_in", "pull_back", "pan_left", "pan_right"], ["none", "minimal"], ["soft_studio", "bright_clean", "cool_modern", "moody_cinematic"]),
            new(TemplateCode: "cinematic_product_showcase", DurationSeconds: 4, Framing: "medium", CameraAngle: "three_quarter", CameraMovement: "push_in", SubjectMotion: "minimal", MotionIntensity: "subtle", Lighting: "soft_studio"));

        var macro = new AiVideoTemplateDefinition("macro_detail_reveal", "Macro Detail Reveal",
            "A close commercial view of the printed artwork and material texture.", 2,
            "Create a photorealistic macro commercial shot of the visible print and surface material detail from the approved source image. The camera performs {{CAMERA_MOVEMENT}} across the marked artwork or detail region from {{CAMERA_ANGLE}}, using {{FRAMING}}. Use shallow depth of field with focus on the selected artwork and material texture. Controlled reflections reveal surface detail without obscuring the design. {{LIGHTING}} Keep the artwork clear and attached to the product surface. Camera motion intensity is {{MOTION_INTENSITY}}. Create a {{DURATION}}-second shot.",
            false, true, Settings(["macro", "close_up"], ["top_down", "three_quarter", "eye_level"], ["locked", "push_in", "pan_left", "pan_right"], ["none"], ["soft_studio", "bright_clean", "moody_cinematic"]),
            new(TemplateCode: "macro_detail_reveal", DurationSeconds: 4, Framing: "macro", CameraAngle: "three_quarter", CameraMovement: "pan_right", SubjectMotion: "none", MotionIntensity: "subtle", Lighting: "soft_studio"));

        var lifestyle = new AiVideoTemplateDefinition("lifestyle_in_use", "Lifestyle In-Use",
            "A natural lifestyle shot of an adult already visible in the approved source image.", 2,
            "Create a polished lifestyle commercial shot of the adult already visible in the approved source image, naturally wearing or using the exact product. The person performs {{PRODUCT_ACTION}} while the product and its artwork remain unobstructed and clearly readable. The camera performs {{CAMERA_MOVEMENT}} at {{CAMERA_ANGLE}} in {{FRAMING}}. {{LIGHTING}} Use natural body movement, realistic fabric physics and an authentic environment. Do not use a pose change that hides the product. Create a {{DURATION}}-second shot.",
            true, false, Settings(["wide", "medium", "medium_close"], ["eye_level", "three_quarter"], ["locked", "push_in", "gentle_tracking"], ["minimal", "natural"], ["warm_natural", "golden_hour", "bright_clean"]),
            new(TemplateCode: "lifestyle_in_use", DurationSeconds: 4, Framing: "medium", CameraAngle: "eye_level", CameraMovement: "gentle_tracking", SubjectMotion: "natural", MotionIntensity: "subtle", Lighting: "warm_natural"));

        var ugc = new AiVideoTemplateDefinition("creator_pov_ugc", "Creator POV / UGC",
            "An authentic creator-style product demonstration with a phone-camera feel.", 2,
            "Create an authentic creator-style product demonstration featuring the adult already visible in the approved source image. Use a realistic phone-camera aesthetic in {{FRAMING}}, {{CAMERA_ANGLE}} composition. The camera uses {{CAMERA_MOVEMENT}} with controlled micro-movement. The creator naturally presents the exact product toward the camera without covering its artwork. {{LIGHTING}} Maintain candid UGC realism, accurate anatomy and stable product details. There is no dialogue, lip-sync or exaggerated performance. Create a {{DURATION}}-second shot.",
            true, false, Settings(["medium_close", "close_up"], ["eye_level", "three_quarter"], ["locked", "push_in", "subtle_handheld"], ["minimal", "natural"], ["bright_clean", "warm_natural"]),
            new(TemplateCode: "creator_pov_ugc", DurationSeconds: 4, Framing: "close_up", CameraAngle: "eye_level", CameraMovement: "subtle_handheld", SubjectMotion: "natural", MotionIntensity: "subtle", Lighting: "bright_clean"));

        var story = new AiVideoTemplateDefinition("everyday_story_moment", "Everyday Story Moment",
            "A warm, grounded everyday scene centered on the product.", 2,
            "Create a warm, emotionally grounded everyday moment centered on the adult already visible in the approved source image and the exact product. The person performs {{PRODUCT_ACTION}} while the camera observes with {{CAMERA_MOVEMENT}} from {{CAMERA_ANGLE}}, using {{FRAMING}}. {{LIGHTING}} Use a natural expression, restrained movement and intimate commercial storytelling. The product remains clearly identifiable throughout. Create a {{DURATION}}-second shot.",
            true, false, Settings(["wide", "medium", "medium_close"], ["eye_level", "three_quarter"], ["locked", "gentle_tracking", "push_in"], ["minimal", "natural"], ["warm_natural", "golden_hour", "moody_cinematic"]),
            new(TemplateCode: "everyday_story_moment", DurationSeconds: 4, Framing: "medium", CameraAngle: "eye_level", CameraMovement: "gentle_tracking", SubjectMotion: "natural", MotionIntensity: "subtle", Lighting: "golden_hour"));

        var reveal = new AiVideoTemplateDefinition("signature_product_reveal", "Signature Product Reveal",
            "A restrained cinematic reveal that finishes on a clean hero frame.", 2,
            "Create a high-impact cinematic reveal of the exact product already shown in the approved source image. Begin from the supplied composition. The camera performs {{CAMERA_MOVEMENT}} from {{CAMERA_ANGLE}}, using {{FRAMING}}, while a restrained lighting transition reveals the product silhouette and artwork. Finish on a clean, stable hero frame suitable for an opening hook or closing shot. {{LIGHTING}} Avoid materialization, particles, smoke, liquid effects and product transformation. Product motion is {{SUBJECT_MOTION}}; camera motion intensity is {{MOTION_INTENSITY}}. Create a {{DURATION}}-second shot.",
            false, false, Settings(["medium", "medium_close", "close_up"], ["three_quarter", "low_angle", "eye_level"], ["locked", "push_in", "orbit_left", "orbit_right"], ["none", "minimal"], ["moody_cinematic", "soft_studio", "cool_modern"]),
            new(TemplateCode: "signature_product_reveal", DurationSeconds: 4, Framing: "medium_close", CameraAngle: "three_quarter", CameraMovement: "orbit_left", SubjectMotion: "minimal", MotionIntensity: "balanced", Lighting: "moody_cinematic"));

        return new[] { showcase, macro, lifestyle, ugc, story, reveal }.ToDictionary(item => item.Code, StringComparer.Ordinal);
    }

    private static string Framing(string value) => value switch
    {
        "wide" => "a wide composition",
        "medium_close" => "a medium close-up composition",
        "close_up" => "a close-up composition",
        "macro" => "a macro close-up composition",
        _ => "a medium composition"
    };
    private static string CameraAngle(string value) => value switch
    {
        "top_down" => "a top-down camera angle",
        "low_angle" => "a subtle low camera angle",
        "three_quarter" => "a three-quarter camera angle",
        _ => "an eye-level camera angle"
    };
    private static string CameraMovement(string value) => value switch
    {
        "locked" => "a locked-off camera",
        "pull_back" => "a slow, smooth pull-back",
        "orbit_left" => "a restrained cinematic orbit to the left",
        "orbit_right" => "a restrained cinematic orbit to the right",
        "pan_left" => "a slow lateral pan to the left",
        "pan_right" => "a slow lateral pan to the right",
        "gentle_tracking" => "gentle tracking movement",
        "subtle_handheld" => "subtle controlled handheld movement",
        _ => "a slow, smooth cinematic push-in"
    };
    private static string StableStudioCameraMovement(string value) => value switch
    {
        "locked" => "locked-off camera",
        "pull_back" => "pull-back",
        "pan_left" => "pan to the left",
        "pan_right" => "pan to the right",
        _ => "push-in"
    };
    private static string StableStudioCameraGuidance(string value) => value switch
    {
        "locked" => "Keep the camera locked off with no reframing.",
        "pull_back" => "Make one smooth, constant-speed pull-back over the full shot; the shirt becomes about 8% smaller by the end while remaining fully visible.",
        "pan_left" => "Make one smooth, constant-speed pan to the left over the full shot; shift the composition gently by about 8% while keeping the full shirt and hanger visible.",
        "pan_right" => "Make one smooth, constant-speed pan to the right over the full shot; shift the composition gently by about 8% while keeping the full shirt and hanger visible.",
        _ => "Make one smooth, constant-speed push-in over the full shot; the shirt becomes about 8% larger by the end while the full shirt and hanger remain visible. Begin and end gently, with no speed jump."
    };
    private static string SubjectMotion(string value) => value switch
    {
        "none" => "none; keep the product still",
        "natural" => "small, believable natural movement",
        _ => "minimal and restrained"
    };
    private static string MotionIntensity(string value) => value == "balanced" ? "balanced and controlled" : "subtle";
    private static string Lighting(string value) => value switch
    {
        "bright_clean" => "Bright, clean natural lighting with clear product visibility.",
        "warm_natural" => "Warm natural light with soft, realistic shadows.",
        "golden_hour" => "Soft golden-hour light with gentle warm highlights.",
        "cool_modern" => "Cool modern studio lighting with restrained reflections.",
        "moody_cinematic" => "Controlled moody cinematic lighting that keeps the artwork readable.",
        _ => "Soft studio lighting creates gentle highlights and natural shadows."
    };
    private static string ProductAction(string productType) => productType.ToLowerInvariant() switch
    {
        "mug" => "gently lift or place the mug",
        "tote bag" => "walk slowly or adjust the tote strap",
        "phone case" => "naturally tilt or hold the phone",
        "poster" => "stand beside or subtly gesture toward the poster",
        "t-shirt" or "hoodie" => "make a slight body turn or subtle sleeve adjustment",
        _ => "make a small natural movement while keeping the product visible"
    };
}
