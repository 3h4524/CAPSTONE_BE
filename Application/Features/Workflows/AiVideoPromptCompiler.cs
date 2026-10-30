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

/// <summary>Normalized P1 settings and the rule text added to one prompt.</summary>
internal sealed record AiPromptRuleApplication(AiVideoSceneOptions Settings, int? Version,
    IReadOnlyList<string> AppliedRules, IReadOnlyList<string> Warnings, string? Guidance, string? PrimaryAction);

internal static class AiVideoPromptRules
{
    internal const int CurrentVersion = 1;

    internal static bool IsRequested(AiVideoSceneOptions options) => options.PromptRuleVersion.HasValue;

    internal static bool Supports(AiVideoTemplateDefinition template) =>
        template.Code == "lifestyle_in_use";

    internal static AiPromptRuleApplication Apply(AiVideoTemplateDefinition template, AiVideoSceneOptions settings)
    {
        if (!IsRequested(settings))
            return new(settings, null, [], [], null, null);

        var defaults = template.Defaults;
        var normalized = settings with
        {
            Framing = defaults.Framing,
            CameraAngle = defaults.CameraAngle,
            CameraMovement = defaults.CameraMovement,
            SubjectMotion = defaults.SubjectMotion,
            MotionIntensity = defaults.MotionIntensity,
            Lighting = defaults.Lighting,
            ExtraInstructions = null
        };
        var startingState = CleanRequired(settings.StartingState!);
        var action = CleanRequired(settings.PrimaryAction!);
        var endingState = CleanRequired(settings.EndingState!);
        var guidance = $"P1 action plan: begin with {startingState}. Perform one primary action only: {action}. Finish with {endingState}. " +
            "Continue the person-product relationship already established by the approved opening image; do not invent a pickup, handoff, wardrobe change, product swap, or extra action. Keep camera movement restrained so it supports the action and does not replace it.";
        return new(normalized, CurrentVersion,
            ["p1-template-default-controls", "p1-start-action-end", "p1-one-primary-action", "p1-person-product-continuity"],
            ["P1 prompt rules use the template defaults; advanced camera, lighting, motion, shared, and custom-direction values are not applied."],
            guidance, action);
    }

    private static string CleanRequired(string value) =>
        new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
}

/// <summary>Builds a provider prompt from a versioned template and validated scene settings.</summary>
public static class AiVideoPromptCompiler
{
    // Keep prompt-template testing deterministic. Re-enable this only when advanced scene controls
    // are ready to be evaluated again in the UI and paid-generation path together.
    internal static bool AdvancedControlsEnabled => false;
    /// <summary>Matches MiniMax's 7,000-character request limit while retaining required preservation rules.</summary>
    internal const int MaximumPromptCharacters = 7_000;
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

    internal static string Compile(AiVideoTemplateDefinition template, AiVideoSceneOptions options, AiVideoProductContext productContext, int durationSeconds, string? sharedInstructions = null, bool stableSingleImage = false) =>
        CompileTrace(template, options, productContext, durationSeconds, sharedInstructions, stableSingleImage).FinalPrompt;

    internal static AiPromptTrace CompileTrace(AiVideoTemplateDefinition template, AiVideoSceneOptions options,
        AiVideoProductContext productContext, int durationSeconds, string? sharedInstructions = null,
        bool stableSingleImage = false, AiPromptRuleApplication? ruleApplication = null,
        IReadOnlyList<VideoPromptExperience>? experiences = null)
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
            .Replace("{{PRODUCT_ACTION}}", ruleApplication?.PrimaryAction ?? ProductAction(productType), StringComparison.Ordinal)
            .Replace("{{DURATION}}", durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var productContextPrompt = ProductContextPrompt(productType, productContext);
        var sceneGuidance = SceneGuidance(template, productType, stableSingleImage);
        var personGuidance = template.RequiresLifestyle
            ? "Keep any adult already visible in the source recognizable and let them move naturally. Do not introduce additional people."
            : null;
        var commonPrompt = EffectiveCommonPrompt(template.CommonPrompt);
        var selectedControls = stableSingleImage && template.Code == "cinematic_product_showcase"
            ? $"Selected scene controls: framing {Framing(settings.Framing)}; {CameraAngle(settings.CameraAngle)}; required camera movement {cameraMovement}; motion intensity {MotionIntensity(settings.MotionIntensity)}; {Lighting(settings.Lighting)}. The product itself remains still while the camera performs the selected movement."
            : $"Selected scene controls: framing {Framing(settings.Framing)}; {CameraAngle(settings.CameraAngle)}; {cameraMovement}; subject motion {SubjectMotion(settings.SubjectMotion)}; motion intensity {MotionIntensity(settings.MotionIntensity)}; {Lighting(settings.Lighting)}";
        var shared = !AdvancedControlsEnabled || string.IsNullOrWhiteSpace(sharedInstructions)
            ? null
            : $"Shared direction for all scenes: {sharedInstructions.Trim()}";
        var custom = string.IsNullOrWhiteSpace(settings.ExtraInstructions) ? null : settings.ExtraInstructions.Trim();
        var sourceGuardrail = SourceGuardrail(productContext, stableSingleImage);
        var templateGuardrail = TemplateGuardrail(template.Code, settings, stableSingleImage);
        var parts = new List<AiPromptPart>();
        AddPart(parts, "template", "Template prompt", prompt);
        AddPart(parts, "prompt_rules", "Prompt rules", ruleApplication?.Guidance);
        AddPart(parts, "product_context", "Product and source context", productContextPrompt);
        AddPart(parts, "scene_guidance", "Scene guidance", sceneGuidance);
        AddPart(parts, "person_guidance", "Person continuity", personGuidance);
        AddPart(parts, "selected_controls", "Effective scene controls", selectedControls);
        AddPart(parts, "shared_instructions", "Shared directions", shared);
        AddPart(parts, "custom_instructions", "Custom direction", custom);
        var ragEnabled = experiences is not null;
        var selectedExperiences = (experiences ?? []).Take(3).ToArray();
        if (selectedExperiences.Length > 0)
        {
            AddPart(parts, "rag", "RAG experience", ExperienceText(selectedExperiences));
        }
        else
        {
            parts.Add(new("rag", "RAG experience", ragEnabled
                ? "No verified experience matched this scene."
                : "Disabled: no retrieved experience was supplied for this prompt snapshot.", false));
        }
        AddPart(parts, "product_guardrails", "Product-preservation rules", commonPrompt);
        AddPart(parts, "source_guardrails", "Source-image rules", sourceGuardrail);
        AddPart(parts, "template_guardrails", "Template-specific rules", templateGuardrail);
        var ragWarnings = new List<string>();
        var finalPrompt = JoinPrompt(parts);
        while (finalPrompt.Length > MaximumPromptCharacters && selectedExperiences.Length > 0)
        {
            selectedExperiences = selectedExperiences[..^1];
            var ragIndex = parts.FindIndex(part => part.Key == "rag");
            parts[ragIndex] = selectedExperiences.Length > 0
                ? new("rag", "RAG experience", ExperienceText(selectedExperiences), true)
                : new("rag", "RAG experience", "No verified experience matched this scene.", false);
            ragWarnings.Add("A low-relevance RAG lesson was omitted to keep the compiled prompt within its limit.");
            finalPrompt = JoinPrompt(parts);
        }
        return new(ruleApplication?.Version, selectedExperiences.Length > 0 ? "applied" : ragEnabled ? "none" : "disabled", ruleApplication?.AppliedRules ?? [],
            (ruleApplication?.Warnings ?? []).Concat(ragWarnings).ToArray(), parts, finalPrompt, selectedExperiences);
    }

    private static void AddPart(List<AiPromptPart> parts, string key, string label, string? content)
    {
        if (!string.IsNullOrWhiteSpace(content)) parts.Add(new(key, label, content, true));
    }

    private static string ExperienceText(IReadOnlyList<VideoPromptExperience> experiences) =>
        "Verified experience for this scene. Apply it only when it does not conflict with the template, source image, or preservation rules:\n"
        + string.Join("\n", experiences.Select(item => $"- {item.Content}"));

    private static string JoinPrompt(IEnumerable<AiPromptPart> parts) => string.Join("\n\n",
        parts.Where(part => part.IncludedInFinalPrompt).Select(part => part.Content));

    private static AiVideoTemplateDefinition WithRequiredGuardrail(AiVideoTemplateDefinition template)
    {
        var defaults = template.Code switch
        {
            "macro_detail_reveal" => template.Defaults with { CameraMovement = "locked", SubjectMotion = "none", MotionIntensity = "subtle" },
            "cinematic_product_showcase" => template.Defaults with { SubjectMotion = "none" },
            _ => template.Defaults
        };
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
            ? "Reviewed-keyframe constraint: use the supplied keyframe as the exact first frame only. Begin the selected camera movement immediately and continue it throughout the shot; the camera must visibly depart from the opening composition when a non-locked movement is selected. Do not use a hold-then-transition, crossfade, dissolve, double exposure or replacement composition. Extend the visible environment naturally as the camera moves while keeping the support, background style and light direction coherent."
            : null;
        return string.Join(" ", new[] { sourceConstraint, keyframeConstraint }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string TemplateGuardrail(string templateCode, AiVideoSceneOptions settings, bool stableSingleImage) => templateCode switch
    {
        "cinematic_product_showcase" when stableSingleImage =>
            $"Showcase camera direction: the selected camera movement is mandatory and is the primary visible action. {StableStudioCameraGuidance(settings.CameraMovement, settings.MotionIntensity)} The product stays attached to its support and does not animate independently. Preserve product identity and set continuity as the viewpoint changes; words such as stable refer to the product, artwork and environment continuity, not to a locked camera. No floating product, invented viewpoint, orbit, camera roll, snap zoom or sudden reframing.",
        "cinematic_product_showcase" =>
            $"Showcase camera direction: the selected camera movement is mandatory and must remain visible from the beginning to the end of the shot. {ShowcaseCameraGuidance(settings.CameraMovement, settings.MotionIntensity)} Keep the product as the visual anchor without freezing the camera; its silhouette, construction and printed design must not drift or transform.",
        "macro_detail_reveal" =>
            $"Macro-detail camera direction: the supplied close-up is the first frame; do not create an establishing shot or a second version of the artwork. The selected movement is mandatory unless Locked is selected. {MacroCameraGuidance(settings.CameraMovement, settings.MotionIntensity)} Keep the focus plane attached to the printed surface and the visible artwork sharply readable while the camera moves. Preserve every visible letter, spelling, line, character feature, color boundary and print edge exactly throughout. Reveal only real surface texture and restrained light response. No artwork redraw, focus pull across lettering, morph, crossfade, ghosting or transition to a newly composed detail.",
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

    private static string MacroCameraGuidance(string value, string intensity)
    {
        var amount = MotionAmount(intensity);
        return value switch
        {
            "locked" => "Keep the camera locked with no zoom, pan, reframing or camera-angle change; only a subtle light sweep may move.",
            "push_in" => $"Push the macro camera forward continuously at a constant speed for the entire shot, making the selected detail about {amount}% larger by the final frame. Begin immediately; do not pause or pull back.",
            "pan_left" => $"Pan the macro camera left continuously at a constant speed across the printed surface for the entire shot. The visible artwork detail travels gradually toward the right side of frame, ending about {amount}% from its starting composition. Keep scale constant and maintain surface focus.",
            "pan_right" => $"Pan the macro camera right continuously at a constant speed across the printed surface for the entire shot. The visible artwork detail travels gradually toward the left side of frame, ending about {amount}% from its starting composition. Keep scale constant and maintain surface focus.",
            _ => "Keep one continuous, clearly visible camera movement throughout the shot without changing to a new viewpoint."
        };
    }

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
        if (!AdvancedControlsEnabled)
            return options with
            {
                TemplateCode = template.Code,
                Framing = defaults.Framing,
                CameraAngle = defaults.CameraAngle,
                CameraMovement = defaults.CameraMovement,
                SubjectMotion = defaults.SubjectMotion,
                MotionIntensity = defaults.MotionIntensity,
                Lighting = defaults.Lighting,
                ExtraInstructions = null
            };
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
        foreach (var row in rows.Where(row => row.IsActive == true && row.Code != null)
                     .OrderBy(row => row.Code, StringComparer.Ordinal)
                     .ThenBy(row => row.TemplateVersion))
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
            "A polished product hero shot with controlled camera movement.", 5,
            "Create one continuous premium e-commerce hero shot from the approved source image. The selected {{CAMERA_MOVEMENT}} is the primary visible action: it begins in the first frame and continues smoothly for the full {{DURATION}} seconds from {{CAMERA_ANGLE}}, using {{FRAMING}}. Keep the product itself still and preserve its exact identity and artwork while the camera moves. {{LIGHTING}} Use realistic materials, coherent background parallax, clean commercial composition and natural shadows. Camera motion intensity is {{MOTION_INTENSITY}}.",
            false, false, Settings(["wide", "medium", "medium_close", "close_up"], ["eye_level", "three_quarter", "low_angle", "top_down"], ["locked", "push_in", "pull_back", "pan_left", "pan_right"], ["none", "minimal"], ["soft_studio", "bright_clean", "cool_modern", "moody_cinematic"]),
            new(TemplateCode: "cinematic_product_showcase", DurationSeconds: 4, Framing: "medium", CameraAngle: "three_quarter", CameraMovement: "push_in", SubjectMotion: "none", MotionIntensity: "subtle", Lighting: "soft_studio"));

        var macro = new AiVideoTemplateDefinition("macro_detail_reveal", "Macro Detail Reveal",
            "A close commercial view of the printed artwork and material texture.", 3,
            "Create one continuous photorealistic macro commercial shot from the approved detail image. The selected {{CAMERA_MOVEMENT}} is the primary visible action and continues smoothly across the artwork for the full {{DURATION}} seconds from {{CAMERA_ANGLE}}, using {{FRAMING}}. Keep the focus plane locked to the printed surface so fine material texture, print edges and all visible lettering remain sharp and readable while the camera moves. {{LIGHTING}} Use controlled reflections without glare or focus hunting. Keep the artwork physically attached to the product. Camera motion intensity is {{MOTION_INTENSITY}}.",
            false, true, Settings(["macro", "close_up"], ["top_down", "three_quarter", "eye_level"], ["locked", "push_in", "pan_left", "pan_right"], ["none"], ["soft_studio", "bright_clean", "moody_cinematic"]),
            new(TemplateCode: "macro_detail_reveal", DurationSeconds: 4, Framing: "macro", CameraAngle: "three_quarter", CameraMovement: "pan_right", SubjectMotion: "none", MotionIntensity: "subtle", Lighting: "soft_studio"));

        var lifestyle = new AiVideoTemplateDefinition("lifestyle_in_use", "Lifestyle In-Use",
            "A natural lifestyle shot of an adult already visible in the approved source image.", 3,
            "Create a polished lifestyle commercial shot of the adult already visible in the approved source image, naturally wearing or using the exact product. The person performs {{PRODUCT_ACTION}} from the established opening relationship. Keep the product and artwork recognizable whenever they are naturally visible; allow realistic folds, perspective, and brief natural occlusion without changing the design. The camera performs {{CAMERA_MOVEMENT}} at {{CAMERA_ANGLE}} in {{FRAMING}}. {{LIGHTING}} Use natural body movement, realistic fabric physics and an authentic environment. Do not introduce a pose change that requires a different person-product relationship. Create a {{DURATION}}-second shot.",
            true, false, Settings(["wide", "medium", "medium_close"], ["eye_level", "three_quarter"], ["locked", "push_in", "gentle_tracking"], ["minimal", "natural"], ["warm_natural", "golden_hour", "bright_clean"]),
            new(TemplateCode: "lifestyle_in_use", DurationSeconds: 4, Framing: "medium", CameraAngle: "eye_level", CameraMovement: "gentle_tracking", SubjectMotion: "natural", MotionIntensity: "subtle", Lighting: "warm_natural"));

        var ugc = new AiVideoTemplateDefinition("creator_pov_ugc", "Creator POV / UGC",
            "An authentic creator-style product demonstration with a phone-camera feel.", 3,
            "Create an authentic creator-style product demonstration featuring the adult already visible in the approved source image. Use a realistic phone-camera aesthetic in {{FRAMING}}, {{CAMERA_ANGLE}} composition. The camera uses {{CAMERA_MOVEMENT}} with controlled micro-movement. The creator naturally presents the exact product toward the camera and avoids deliberately hiding its main design; natural hand overlap, folds, and perspective are allowed when product identity remains stable. {{LIGHTING}} Maintain candid UGC realism, accurate anatomy and stable product details. There is no dialogue, lip-sync or exaggerated performance. Create a {{DURATION}}-second shot.",
            true, false, Settings(["medium_close", "close_up"], ["eye_level", "three_quarter"], ["locked", "push_in", "subtle_handheld"], ["minimal", "natural"], ["bright_clean", "warm_natural"]),
            new(TemplateCode: "creator_pov_ugc", DurationSeconds: 4, Framing: "close_up", CameraAngle: "eye_level", CameraMovement: "subtle_handheld", SubjectMotion: "natural", MotionIntensity: "subtle", Lighting: "bright_clean"));

        var story = new AiVideoTemplateDefinition("everyday_story_moment", "Everyday Story Moment",
            "A warm, grounded everyday scene centered on the product.", 3,
            "Create a warm, emotionally grounded everyday moment centered on the adult already visible in the approved source image and the exact product. The person performs {{PRODUCT_ACTION}} while the camera observes with {{CAMERA_MOVEMENT}} from {{CAMERA_ANGLE}}, using {{FRAMING}}. {{LIGHTING}} Use a natural expression, restrained movement and intimate commercial storytelling. Keep product identity stable and recognizable whenever it is naturally visible; do not require artificial full visibility in every frame. Create a {{DURATION}}-second shot.",
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
        "pan_left" => "a slow horizontal camera pan to the left",
        "pan_right" => "a slow horizontal camera pan to the right",
        "gentle_tracking" => "gentle tracking movement",
        "subtle_handheld" => "subtle controlled handheld movement",
        _ => "a slow, smooth cinematic push-in"
    };
    private static string StableStudioCameraMovement(string value) => value switch
    {
        "locked" => "locked-off camera",
        "pull_back" => "pull-back",
        "pan_left" => "a smooth horizontal pan to the left",
        "pan_right" => "a smooth horizontal pan to the right",
        _ => "push-in"
    };
    private static string StableStudioCameraGuidance(string value, string intensity)
    {
        var amount = MotionAmount(intensity);
        return value switch
        {
            "locked" => "Keep the camera locked off with no reframing.",
            "pull_back" => $"Pull the camera back continuously at a constant speed for the entire shot; the product becomes about {amount}% smaller by the final frame while remaining fully visible.",
            "pan_left" => $"From the opening frame, pan the camera left continuously at a constant speed for the entire shot. Show visible horizontal background motion and natural parallax; the product shifts gradually toward the right side of frame, ending about {amount}% from its starting composition.",
            "pan_right" => $"From the opening frame, pan the camera right continuously at a constant speed for the entire shot. Show visible horizontal background motion and natural parallax; the product shifts gradually toward the left side of frame, ending about {amount}% from its starting composition.",
            _ => $"Push the camera in continuously at a constant speed for the entire shot; the product becomes about {amount}% larger by the final frame while remaining fully visible."
        };
    }

    private static string ShowcaseCameraGuidance(string value, string intensity)
    {
        var amount = MotionAmount(intensity);
        return value switch
        {
            "locked" => "Keep the camera locked off; allow only restrained natural shadow or material movement.",
            "pull_back" => $"Pull back continuously so the product becomes about {amount}% smaller by the final frame.",
            "pan_left" => $"Pan left continuously with visible background parallax; the product ends about {amount}% to the right of its starting position in the composition.",
            "pan_right" => $"Pan right continuously with visible background parallax; the product ends about {amount}% to the left of its starting position in the composition.",
            _ => $"Push in continuously so the product becomes about {amount}% larger by the final frame."
        };
    }

    private static int MotionAmount(string intensity) => intensity == "balanced" ? 16 : 10;
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
