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
    string? CommonPrompt = null,
    IReadOnlyList<AiVideoSubtemplateDefinition>? Subtemplates = null,
    string? DefaultSubtemplateCode = null);

/// <summary>A concrete prompt and default shot setup within a broader AI video template.</summary>
public sealed record AiVideoSubtemplateDefinition(
    string Code,
    string Name,
    string Description,
    string BasePrompt,
    AiVideoSceneOptions Defaults,
    string? StartFramePrompt = null);

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

        var defaults = AiVideoPromptCompiler.ResolveDefaults(template, settings.SubtemplateCode);
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
    internal const string ProfileOrbitGuardrailKey = "profile_orbit_guardrails";
    internal const string FabricDetailGuardrailKey = "fabric_detail_guardrails";
    internal const string BrandFocusGuardrailKey = "brand_focus_guardrails";
    private const string MiniMaxH3FabricDetailPrompt = "One continuous {{DURATION}}-second product-detail shot develops from the approved opening image. {{PRODUCT_CONTEXT}} The opening crop establishes one visible product detail; follow that same region as the shot develops, retaining its existing texture, construction and any visible print edges or lettering. {{DETAIL_MOTION}} Starting from {{CAMERA_ANGLE}}, the camera arcs around the model toward frame-right with small amplitude at slow speed. Begin gently, glide along one short, shallow curve, then ease softly to a stop at a slightly more side-on view of the same detail. Keep approximately the same camera height, distance, focal length and detail scale, with the lens aimed at the same product region throughout. Restrained background parallax and natural surface foreshortening make the arc visible. Shorten the arc if the opening crop is already tight or the detail nears the frame edge. Stop before passing behind the shoulder or revealing the back. Keep the existing visible detail within the frame and in focus; do not reconstruct parts of the artwork cropped out of the opening image. No push-in, zoom, pull-back, focus pull or switch to another detail. {{LIGHTING}} Preserve the source setting and natural shadows.";
    private const string MiniMaxH3BrandFocusPrompt = "One continuous {{DURATION}}-second brand-focused product-on-model shot develops from the approved opening image. {{PRODUCT_CONTEXT}} Frame the model and product together, with the existing visible artwork, lettering or logo as the composition anchor. If no branding is visible, the existing silhouette, colors and construction remain the visual focus instead. {{BRAND_MOTION}} At the same time, from the opening frame the camera pushes in with small amplitude at slow speed, maintaining {{CAMERA_ANGLE}}. The shallow approach develops smoothly across the shot and eases to a stop while the model's visible expression and the same design remain clear. Keep the head and face in frame when visible in the opening image, together with the opening design area; shorten the approach whenever needed to retain both. Do not crop tighter onto the logo alone, rack focus between the face and design, or reveal a face absent from the opening crop. Finish with the model and existing design or unbranded product sharing the composition. {{LIGHTING}} Preserve the source setting and natural shadows.";
    private const string FabricDetailGuardrail = "Detail continuity and camera-path constraint: follow only the same product detail established by the opening crop. The camera arcs around the model toward frame-right with small amplitude at slow speed, keeping approximately the same height, distance, focal length and detail scale. Use a stabilized shallow curve with gentle starts and stops; shorten it to keep the visible detail within the frame and stop before passing behind the shoulder or revealing the back. No push-in, zoom, pull-back, full orbit or focus pull. Allow natural perspective and foreshortening while keeping the existing detail in focus. Do not switch to another detail, invent stitching or material features, or reconstruct hidden, cropped-out or unreadable print and lettering. Existing artwork stays attached to the same surface with its content and placement unchanged. The model keeps the opening body orientation unless an explicit P1 action plan requires otherwise; P1 takes precedence over default body actions. Retain realistic product motion and the source lighting and setting.";
    private const string BrandFocusGuardrail = "Brand visibility constraint: keep the design area visible in the opening image within the frame and in focus during a shallow, slow push-in from the source viewing direction, without orbiting or turning it into a macro texture shot. Preserve existing artwork, logo geometry, lettering, colors and placement on the same product surface, allowing natural folds and perspective. Do not invent branding, complete hidden design areas, repair or transcribe unreadable lettering, or flatten or redraw the product to face the lens. If no branding is visible, retain the existing silhouette, colors and construction as the focus. The explicit P1 action plan takes precedence over default body actions; preserve source lighting and setting.";
    private const string BrandFocusPerformanceGuardrail = "The model gently lifts their gaze upward, then follows with a small, relaxed lift of the chin and settles looking slightly above the lens. Keep this one natural upward-looking action within a comfortable range, without tilting the head far back, walking or turning the torso. The torso keeps its opening orientation and the hands keep their original placement. Keep the head and face in frame when already visible, together with the existing design; do not push into a logo-only crop or invent a face outside the opening image.";
    private const string MiniMaxH3FirstFrame = "For the target video, at 0.00 seconds into the target video, <Picture 1> (from [Shot 1]) is fully referenced.";
    private const string MiniMaxH3NaturalMotionPrompt = "Create one continuous {{DURATION}}-second product-on-model advertisement from the approved source image. {{PRODUCT_CONTEXT}} {{NATURAL_MOTION}} The camera performs {{CAMERA_MOVEMENT}}, starting immediately and continuing smoothly throughout the shot. Retain {{CAMERA_ANGLE}} and the opening composition, allowing only a small change in framing. Keep the model's head and product artwork in frame when visible in the opening image. {{LIGHTING}} Preserve the source setting and natural shadows.";
    private const string MiniMaxH3ProfileTrackingPrompt = "One continuous {{DURATION}}-second product-on-model shot develops from the approved opening image. {{PRODUCT_CONTEXT}} {{PROFILE_MOTION}} The camera begins at {{CAMERA_ANGLE}} and arcs around the model toward the right-hand side of the opening image with small amplitude at slow speed. Spread a short, shallow arc of about 30-40 degrees in total across the entire shot, never more than 40 degrees from the opening viewing direction. The movement starts gently, glides at an unhurried, even pace, then eases softly to a stop at the end, with stable gimbal-like motion and no shake, abrupt acceleration or whip movement. Visible but restrained background parallax establishes that the camera is actually moving. The final view is slightly more side-on, remaining within the front three-quarter to side-profile range; stop at the side-profile boundary without passing behind the shoulder or revealing the back. If the opening image is already close to profile, shorten the arc to stay on the front-facing side of that boundary. Camera height and distance remain approximately constant, with the model centered at approximately the same size and the product in focus. The front artwork becomes naturally foreshortened as the angle changes; its content and placement remain unchanged. {{LIGHTING}} Preserve the source setting and natural shadows.";
    private const string ProfileOrbitGuardrail = "Camera-path and design-visibility constraint: use a slow, smooth, shallow arc toward frame-right, about 30-40 degrees across the whole shot and never more than 40 degrees from the opening view. Shorten the path when needed to stop at the side-profile boundary. Do not travel behind the model or reveal the back. Keep the movement stabilized and unhurried, with gentle starts and stops rather than a fast sweep, running-camera bounce or whip pan. Do not turn the model merely to keep facing the camera; an explicitly selected action plan still takes precedence for the person's actions. Allow natural foreshortening of the front design as the angle changes. Do not twist or redraw the product to keep that design facing the lens. Never copy the front graphic onto the back or add artwork, lettering, logos or unsupported construction details to newly revealed surfaces.";
    public const string CommonGuardrail = "Product identity constraint for the entire shot: use the approved source image as the authoritative visual reference and keep the same exact product in every frame. Preserve its visible construction, silhouette, proportions, main colors, material character, distinctive details, and the placement, scale, geometry, colors, spelling and lettering of every visible artwork or logo. Do not redraw, replace, duplicate, morph or reinterpret the product or its printed design. Natural perspective, fabric folds and physically plausible material movement are allowed only when required by the selected action; they must not change the design content or detach it from the product surface. Create one continuous shot without cuts, dissolves, ghosted duplicates or sudden reframing. Do not introduce unrelated text, watermarks or extra products. No dialogue, speech, singing or music; use quiet ambient room tone only. These identity and continuity constraints take precedence over any earlier direction that conflicts with them.";
    private const string ProductOnModelGuardrail = "Use the approved source image as the authoritative reference. Keep the same model and exact product throughout the shot. Preserve the product's visible fit, construction, colors, artwork, logos, lettering and placement; allow only natural changes caused by posture, fabric folds, perspective and brief occlusion. Keep anatomy and fabric motion realistic. Use one continuous shot with no cuts, morphing, duplicate people or products, wardrobe changes, added text, watermarks, dialogue or music.";
    private const string LegacyProfileStartFramePrompt = "Create a photorealistic medium eye-level fashion keyframe from a three-quarter side angle. The model stands still with feet planted, torso turned about 45 degrees, neutral head and relaxed arms away from the artwork. Keep enough garment front visible for the design and leave lateral space for an immediate sideways camera track. No walking pose.";
    private const string LegacyProfileTrackingPrompt = "Create one continuous {{DURATION}}-second live-action product-on-model fashion advertisement using the approved source image as the opening frame. {{PRODUCT_CONTEXT}} {{PROFILE_MOTION}} From the first frame, the camera arcs slowly to camera-right around the model, moving toward the right-hand side of the opening image along a short, shallow curve. Keep approximately the same camera height and distance, with the lens aimed at the upper torso. Continue this movement smoothly throughout the shot. Preserve the background and setting shown in the source image. Keep approximately the same framing throughout the camera arc. Any visible design remains unchanged and attached to the same area of the garment. As the camera moves around the model, view the design from the changing camera angle. Do not redraw, stretch or rearrange the design. {{LIGHTING}}";
    private const string LegacyFabricStartFramePrompt = "Create a photorealistic close editorial keyframe of the model wearing the exact product, cropped around the upper torso and a real fabric or artwork detail. Keep texture, stitching, print edges, lettering and colors sharp and unchanged. Use a slight three-quarter angle, soft natural light and no invented details.";
    internal const string LegacyFabricDetailPushPrompt = "Create one continuous {{DURATION}}-second live-action editorial product-detail advertisement using the approved source image as the opening frame. {{PRODUCT_CONTEXT}} Follow the same visible product detail already emphasized by the opening crop throughout the shot. Preserve that region's existing texture, construction and any visible print edges or lettering. Do not switch to a different detail or add features absent from the source. {{DETAIL_MOTION}} From the first frame, the camera performs {{CAMERA_MOVEMENT}}, continuing in a steady, uninterrupted glide. Keep the approach shallow and the change in framing small. Maintain {{CAMERA_ANGLE}} and keep focus on the same product surface throughout, without a focus pull or sudden reframing. Preserve the background and setting shown in the source image. {{LIGHTING}}";
    private const string LegacyBrandFocusPrompt = "Create one continuous medium product-on-model editorial shot from the approved source image. Use torso framing and keep the garment's visible artwork, lettering, or logo as the visual focus; if the garment has no branding, emphasize its real silhouette, color blocking, and construction instead. The camera performs {{CAMERA_MOVEMENT}} at {{CAMERA_ANGLE}} in {{FRAMING}} for the full {{DURATION}} seconds. The model makes a small confident posture adjustment while keeping the product naturally visible. {{LIGHTING}} Preserve accessories only when they already exist in the source. Use natural fabric folds, warm commercial color, shallow depth of field, and restrained film grain.";
    private const string LegacyBrandStartFramePrompt = "Create a photorealistic medium-close, eye-level fashion keyframe that is nearly front-facing. Center the garment artwork, logo or silhouette and keep it flat, readable and fully unobstructed. The model stands naturally with hands outside the product area, clean commercial light and a simple coherent background.";

    private static readonly IReadOnlyDictionary<string, AiVideoTemplateDefinition> BuiltIns = CreateBuiltIns();

    /// <summary>
    /// Whether a template shows a person using or wearing the product, so its scene has to start from a photo that
    /// already has one: the design's Lifestyle mock-up, made from a model photo at Mockup Approval.
    /// </summary>
    public static bool RequiresPerson(string? templateCode) =>
        BuiltIns.GetValueOrDefault(AiStudioFrameIdentity.NormalizeTemplateCode(templateCode))?.RequiresLifestyle ?? false;

    public static IReadOnlyList<AiVideoTemplateDefinition> Catalog(IEnumerable<VideoTemplate>? persisted = null)
    {
        var stored = ReadPersisted(persisted);
        return BuiltIns.Values.Select(item => stored.TryGetValue(item.Code, out var overrideValue)
                ? PreferCurrentDefinition(item, overrideValue)
                : item)
            .Select(WithRequiredGuardrail)
            .OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
    }

    public static AiVideoTemplateDefinition? Resolve(string code, IEnumerable<VideoTemplate>? persisted = null)
    {
        var stored = ReadPersisted(persisted);
        var builtIn = BuiltIns.GetValueOrDefault(code);
        var resolved = stored.TryGetValue(code, out var fromDatabase) && builtIn is not null
            ? PreferCurrentDefinition(builtIn, fromDatabase)
            : fromDatabase ?? builtIn;
        return resolved is null ? null : WithRequiredGuardrail(resolved);
    }

    private static AiVideoTemplateDefinition PreferCurrentDefinition(
        AiVideoTemplateDefinition builtIn,
        AiVideoTemplateDefinition persisted)
    {
        if (builtIn.Code != "lifestyle_in_use") return persisted;
        if (persisted.PromptVersion < builtIn.PromptVersion)
        {
            // Upgrade the affected subtemplates while preserving other persisted directions.
            var oldNatural = persisted.Subtemplates?.FirstOrDefault(item => item.Code == "natural_motion");
            var currentNatural = builtIn.Subtemplates?.FirstOrDefault(item => item.Code == "natural_motion");
            var oldProfile = persisted.Subtemplates?.FirstOrDefault(item => item.Code == "profile_tracking");
            var currentProfile = builtIn.Subtemplates?.FirstOrDefault(item => item.Code == "profile_tracking");
            var currentFabric = builtIn.Subtemplates?.FirstOrDefault(item => item.Code == "fabric_detail");
            var currentBrand = builtIn.Subtemplates?.FirstOrDefault(item => item.Code == "brand_focus");
            if (persisted.PromptVersion is not (6 or 7 or 8 or 9 or 10 or 11) || oldNatural is null || currentNatural is null ||
                oldProfile is null || currentProfile is null || currentFabric is null || currentBrand is null)
                return builtIn;

            var allowedSettings = persisted.AllowedSettings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            foreach (var (key, value) in new[] { ("cameraMovement", "orbit_right"), ("lighting", "source_image"), ("cameraAngle", "source_view") })
                allowedSettings[key] = (allowedSettings.GetValueOrDefault(key) ?? []).Append(value).Distinct(StringComparer.Ordinal).ToArray();
            persisted = persisted with
            {
                PromptVersion = builtIn.PromptVersion,
                BasePrompt = persisted.PromptVersion < 9 && persisted.BasePrompt == oldNatural.BasePrompt ? builtIn.BasePrompt : persisted.BasePrompt,
                AllowedSettings = allowedSettings,
                Subtemplates = persisted.Subtemplates!.Select(item => item.Code switch
                {
                    "natural_motion" when persisted.PromptVersion < 9 => item with { BasePrompt = currentNatural.BasePrompt },
                    "fabric_detail" when persisted.PromptVersion < 10 => item with
                    {
                        BasePrompt = currentFabric.BasePrompt,
                        Description = currentFabric.Description,
                        Defaults = item.Defaults with { CameraMovement = "orbit_right", CameraAngle = "source_view", Lighting = "source_image" },
                        StartFramePrompt = string.IsNullOrWhiteSpace(item.StartFramePrompt) || item.StartFramePrompt == LegacyFabricStartFramePrompt
                            ? currentFabric.StartFramePrompt : item.StartFramePrompt
                    },
                    "profile_tracking" when persisted.PromptVersion < 8 => item with
                    {
                        Name = currentProfile.Name,
                        Description = currentProfile.Description,
                        BasePrompt = currentProfile.BasePrompt,
                        Defaults = item.Defaults with { CameraMovement = "orbit_right", Lighting = "source_image" },
                        StartFramePrompt = string.IsNullOrWhiteSpace(item.StartFramePrompt) || item.StartFramePrompt == LegacyProfileStartFramePrompt
                            ? currentProfile.StartFramePrompt : item.StartFramePrompt
                    },
                    "profile_tracking" when item.BasePrompt == LegacyProfileTrackingPrompt => item with
                    {
                        BasePrompt = currentProfile.BasePrompt
                    },
                    "brand_focus" when item.BasePrompt == LegacyBrandFocusPrompt => item with
                    {
                        BasePrompt = currentBrand.BasePrompt,
                        Description = currentBrand.Description,
                        Defaults = item.Defaults with { CameraMovement = "push_in", CameraAngle = "source_view", Lighting = "source_image" },
                        StartFramePrompt = string.IsNullOrWhiteSpace(item.StartFramePrompt) || item.StartFramePrompt == LegacyBrandStartFramePrompt
                            ? currentBrand.StartFramePrompt : item.StartFramePrompt
                    },
                    _ => item
                }).ToArray()
            };
        }

        // Recognize the former code-owned detail prompt even in current-version catalog rows.
        // Preserve custom creative text and camera defaults other than the former push-in.
        var fabric = builtIn.Subtemplates!.Single(item => item.Code == "fabric_detail");
        var upgradedFabricCamera = persisted.Subtemplates?.Any(item => item.Code == "fabric_detail" &&
            item.BasePrompt == LegacyFabricDetailPushPrompt && item.Defaults.CameraMovement == "push_in") == true;
        var fabricAllowedSettings = persisted.AllowedSettings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (upgradedFabricCamera)
            fabricAllowedSettings["cameraMovement"] = (fabricAllowedSettings.GetValueOrDefault("cameraMovement") ?? [])
                .Append("orbit_right").Distinct(StringComparer.Ordinal).ToArray();
        persisted = persisted with
        {
            AllowedSettings = fabricAllowedSettings,
            Subtemplates = persisted.Subtemplates?.Select(item => item.Code == "fabric_detail" &&
                item.BasePrompt == LegacyFabricDetailPushPrompt
                    ? item with
                    {
                        BasePrompt = fabric.BasePrompt,
                        Description = fabric.Description,
                        Defaults = item.Defaults with
                        {
                            CameraMovement = item.Defaults.CameraMovement == "push_in" ? "orbit_right" : item.Defaults.CameraMovement
                        }
                    } : item).ToArray()
        };

        // Older catalog rows predate the keyframe-direction field. Preserve the editable persisted
        // video prompt while filling this non-breaking addition from the code-owned definition.
        var builtInsByCode = (builtIn.Subtemplates ?? []).ToDictionary(item => item.Code, StringComparer.Ordinal);
        var subtemplates = (persisted.Subtemplates ?? []).Select(item =>
            string.IsNullOrWhiteSpace(item.StartFramePrompt) && builtInsByCode.TryGetValue(item.Code, out var current)
                ? item with { StartFramePrompt = current.StartFramePrompt }
                : item).ToArray();
        return persisted with { Subtemplates = subtemplates };
    }

    internal static string Compile(AiVideoTemplateDefinition template, AiVideoSceneOptions options, AiVideoProductContext productContext, int durationSeconds, string? sharedInstructions = null, bool stableSingleImage = false) =>
        CompileTrace(template, options, productContext, durationSeconds, sharedInstructions, stableSingleImage).FinalPrompt;

    internal static AiPromptTrace CompileTrace(AiVideoTemplateDefinition template, AiVideoSceneOptions options,
        AiVideoProductContext productContext, int durationSeconds, string? sharedInstructions = null,
        bool stableSingleImage = false, AiPromptRuleApplication? ruleApplication = null,
        IReadOnlyList<VideoPromptExperience>? experiences = null, string? modelKey = null)
    {
        var settings = ApplyDefaults(template, options, modelKey);
        // A null model produces the provider-neutral draft. Economy and Premium share the
        // application prompt format; model routing and pricing remain provider-specific.
        var useH3Format = AiVideoModel.UsesStructuredPrompt(modelKey);
        var subtemplate = ResolveSubtemplate(template, settings.SubtemplateCode);
        var useH3NaturalMotion = useH3Format && template.Code == "lifestyle_in_use" && subtemplate?.Code == "natural_motion";
        var cameraMovement = stableSingleImage && template.Code == "cinematic_product_showcase"
            ? StableStudioCameraMovement(settings.CameraMovement)
            : useH3NaturalMotion
            ? MiniMaxH3CameraMovement(settings.CameraMovement)
            : CameraMovement(settings.CameraMovement);
        var productType = NormalizeProductType(productContext.ProductType);
        // Preserve persisted/custom creative directions. Concise rewrites apply only to the
        // code-owned Product On Model prompts, not seller-edited text.
        var basePrompt = subtemplate?.BasePrompt ?? template.BasePrompt;
        var rewriteNaturalMotion = useH3NaturalMotion && basePrompt == BuiltIns["lifestyle_in_use"].Subtemplates!
            .Single(item => item.Code == "natural_motion").BasePrompt;
        var rewriteProfileTracking = useH3Format && template.Code == "lifestyle_in_use" &&
            subtemplate?.Code == "profile_tracking" && basePrompt == BuiltIns["lifestyle_in_use"].Subtemplates!
                .Single(item => item.Code == "profile_tracking").BasePrompt;
        var rewriteFabricDetail = useH3Format && settings.CameraMovement == "orbit_right" && template.Code == "lifestyle_in_use" &&
            subtemplate?.Code == "fabric_detail" && basePrompt == BuiltIns["lifestyle_in_use"].Subtemplates!
                .Single(item => item.Code == "fabric_detail").BasePrompt;
        var rewriteBrandFocus = useH3Format && settings.CameraMovement == "push_in" && template.Code == "lifestyle_in_use" &&
            subtemplate?.Code == "brand_focus" && basePrompt == BuiltIns["lifestyle_in_use"].Subtemplates!
                .Single(item => item.Code == "brand_focus").BasePrompt;
        var prompt = (rewriteNaturalMotion ? MiniMaxH3NaturalMotionPrompt
            : rewriteProfileTracking ? MiniMaxH3ProfileTrackingPrompt
            : rewriteFabricDetail ? MiniMaxH3FabricDetailPrompt
            : rewriteBrandFocus ? MiniMaxH3BrandFocusPrompt : basePrompt)
            .Replace("{{FRAMING}}", Framing(settings.Framing), StringComparison.Ordinal)
            .Replace("{{CAMERA_ANGLE}}", CameraAngle(settings.CameraAngle), StringComparison.Ordinal)
            .Replace("{{CAMERA_MOVEMENT}}", cameraMovement, StringComparison.Ordinal)
            .Replace("{{SUBJECT_MOTION}}", SubjectMotion(settings.SubjectMotion), StringComparison.Ordinal)
            .Replace("{{MOTION_INTENSITY}}", MotionIntensity(settings.MotionIntensity), StringComparison.Ordinal)
            .Replace("{{LIGHTING}}", Lighting(settings.Lighting), StringComparison.Ordinal)
            .Replace("{{DETAIL_MOTION}}", ruleApplication?.PrimaryAction is null
                ? DetailSubjectMotion(productType) + " Keep the original hand placement. No deliberate gestures, steps or body turns."
                : "Continue naturally from the approved opening image while following the action plan.", StringComparison.Ordinal)
            .Replace("{{NATURAL_MOTION}}", ruleApplication?.PrimaryAction is null
                ? rewriteNaturalMotion
                    ? "Starting from the original pose, the model takes one short, relaxed step toward the camera with the right foot and gently transfers their weight onto it. The torso and shoulders follow the step naturally. Keep the original hand placement; a hand already resting in a pocket stays there. " + NaturalProductMotion(productType) + " After settling into the new stance, the model glances toward the right-hand side of the frame. The head follows with a small, relaxed turn while the torso maintains its direction. Finish comfortably balanced, looking slightly off-camera, with gentle breathing. No additional steps or exaggerated posing."
                    : "Starting immediately from the pose in the image, the model takes one short, relaxed step toward the camera with the right foot. As the foot lands, the model transfers their weight onto it. The hips and upper body move forward together, with a small natural balancing movement in the shoulders and any free arm. A hand already resting in a pocket stays there. " + NaturalProductMotion(productType) + " Once comfortably balanced in the new stance, the model briefly glances toward the right-hand side of the frame. The head gently follows the gaze with a small, relaxed turn while the shoulders and torso keep their direction. Finish looking slightly off-camera, with a relaxed expression and gentle breathing. Perform one short step followed by this small head turn, with soft, unhurried movement. No continuous walking, sudden head movements or exaggerated posing."
                : "Continue naturally from the approved opening image while following the action plan.", StringComparison.Ordinal)
            .Replace("{{PROFILE_MOTION}}", ruleApplication?.PrimaryAction is null
                ? rewriteProfileTracking
                    ? "The model remains in the opening position with the same body orientation, head direction and gaze. " + DetailSubjectMotion(productType) + " The hands keep their original placement. The model does not walk, step or turn to follow the camera."
                    : "The model remains comfortably in the same spot, keeping the body, head and gaze oriented in the direction established by the opening image as the camera moves around them. Gentle breathing creates small, natural movement in the chest, shoulders and fabric. Keep the original hand placement. The model does not turn to follow the camera. No walking, stepping or deliberate body turns."
                : "Continue naturally from the approved opening image while following the action plan.", StringComparison.Ordinal)
            .Replace("{{BRAND_MOTION}}", ruleApplication?.PrimaryAction is null
                ? "Starting from the pose in the opening image, the model gently lifts their gaze upward, then follows with a small, relaxed lift of the chin. Keep the movement soft and unhurried, as if noticing something slightly above the lens, while the shoulders remain relaxed and the torso keeps its original direction. If the opening gaze is already raised, use only a slight comfortable adjustment without tilting the head far back. Keep the original hand placement. " + NaturalProductMotion(productType) + " Settle into the gently raised gaze and continue with natural breathing. No steps, deliberate body turns or gestures across the product."
                : "Continue naturally from the approved opening image while following the action plan.", StringComparison.Ordinal)
            .Replace("{{PRODUCT_ACTION}}", ruleApplication?.PrimaryAction ?? ProductAction(productType), StringComparison.Ordinal)
            .Replace("{{DURATION}}", durationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var isProductOnModel = template.Code == "lifestyle_in_use";
        var hasOpeningProductContext = isProductOnModel && subtemplate?.Code is "natural_motion" or "profile_tracking" or "fabric_detail" or "brand_focus";
        var productContextPrompt = hasOpeningProductContext
            ? $"Product: {productType}. Keep the product and any visible design as the visual focus. Match the approved image for its visible design, colors, fit and proportions."
            : ProductContextPrompt(template.Code, productType, productContext);
        if (rewriteProfileTracking)
            productContextPrompt = $"The shot features the same {productType} from the opening image. Its design, colors, fit and proportions remain consistent during the shallow change in viewpoint. Preserve artwork where visible, allowing natural perspective and foreshortening.";
        if (rewriteFabricDetail || rewriteBrandFocus)
            productContextPrompt = $"The same {productType} remains on the same model as in the opening image, with its visible design, colors, construction and proportions preserved.";
        var sceneGuidance = isProductOnModel ? null : SceneGuidance(template, productType, stableSingleImage);
        var personGuidance = template.RequiresLifestyle && !isProductOnModel
            ? "Keep any person already visible in the source recognizable and let them move naturally. Do not introduce additional people."
            : null;
        var commonPrompt = isProductOnModel
            ? ProductOnModelGuardrail
            : EffectiveCommonPrompt(template.CommonPrompt);
        if (rewriteProfileTracking)
            commonPrompt = commonPrompt.Replace("perspective and brief occlusion.",
                "perspective and natural occlusion as surfaces turn out of view.", StringComparison.Ordinal);
        var selectedControls = isProductOnModel
            ? null
            : stableSingleImage && template.Code == "cinematic_product_showcase"
            ? $"Selected scene controls: framing {Framing(settings.Framing)}; {CameraAngle(settings.CameraAngle)}; required camera movement {cameraMovement}; motion intensity {MotionIntensity(settings.MotionIntensity)}; {Lighting(settings.Lighting)}. The product itself remains still while the camera performs the selected movement."
            : $"Selected scene controls: framing {Framing(settings.Framing)}; {CameraAngle(settings.CameraAngle)}; {cameraMovement}; subject motion {SubjectMotion(settings.SubjectMotion)}; motion intensity {MotionIntensity(settings.MotionIntensity)}; {Lighting(settings.Lighting)}";
        var shared = !AdvancedControlsEnabled || string.IsNullOrWhiteSpace(sharedInstructions)
            ? null
            : $"Shared direction for all scenes: {sharedInstructions.Trim()}";
        var custom = string.IsNullOrWhiteSpace(settings.ExtraInstructions) ? null : settings.ExtraInstructions.Trim();
        var sourceGuardrail = isProductOnModel ? null : SourceGuardrail(productContext, stableSingleImage);
        var templateGuardrail = isProductOnModel ? null : TemplateGuardrail(template.Code, settings, stableSingleImage);
        var parts = new List<AiPromptPart>();
        var contextMarker = "{{PRODUCT_CONTEXT}}";
        var contextPosition = hasOpeningProductContext ? prompt.IndexOf(contextMarker, StringComparison.Ordinal) : -1;
        if (contextPosition >= 0)
        {
            AddPart(parts, "template", "Template prompt", prompt[..contextPosition].Trim());
            AddPart(parts, "product_context", "Product and source context", productContextPrompt);
            AddPart(parts, "template_direction", "Motion and camera direction", prompt[(contextPosition + contextMarker.Length)..].Trim());
            AddPart(parts, "prompt_rules", "Prompt rules", ruleApplication?.Guidance);
        }
        else
        {
            AddPart(parts, "template", "Template prompt", prompt);
            AddPart(parts, "prompt_rules", "Prompt rules", ruleApplication?.Guidance);
            AddPart(parts, "product_context", "Product and source context", productContextPrompt);
        }
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
        if (rewriteProfileTracking)
            AddPart(parts, ProfileOrbitGuardrailKey, "Profile orbit and design visibility", ProfileOrbitGuardrail);
        if (rewriteFabricDetail)
            AddPart(parts, FabricDetailGuardrailKey, "Opening detail continuity", FabricDetailGuardrail);
        if (rewriteBrandFocus)
            AddPart(parts, BrandFocusGuardrailKey, "Brand visibility and model performance", BrandFocusGuardrail +
                (ruleApplication?.PrimaryAction is null ? " " + BrandFocusPerformanceGuardrail : string.Empty));
        if (useH3Format) ApplyMiniMaxH3Format(parts);
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

    private static void ApplyMiniMaxH3Format(List<AiPromptPart> parts)
    {
        // Keep each sent fragment in the trace so review, quote, snapshot and provider payload
        // all use exactly the same string. Include this overhead before trimming optional RAG.
        parts.Insert(0, new("h3_i2va_v1", "MiniMax H3 I2VA format v1", MiniMaxH3FirstFrame, true));
        parts.Insert(1, new("h3_visual_description", "H3 shot and opening image",
            "integrated_multimodal_description: [Shot 1] The opening visual style, composition, subject appearance, product, setting and lighting match <Picture 1>. Continue from that opening state with the following movement and preservation requirements.", true));
        var guardrailIndex = parts.FindIndex(part => part.Key == "product_guardrails");
        var guardrail = parts[guardrailIndex];
        parts[guardrailIndex] = guardrail with
        {
            Content = guardrail.Content
                .Replace(" No dialogue, speech, singing or music; use quiet ambient room tone only.", string.Empty, StringComparison.Ordinal)
                .Replace("watermarks, dialogue or music.", "watermarks.", StringComparison.Ordinal)
        };
        AddPart(parts, "h3_soundscape", "H3 ambient sound",
            "overall_soundscape: Quiet ambient sound only. No dialogue, speech, singing or diegetic music.");
        AddPart(parts, "h3_music", "H3 background music", "non_diegetic_music: N/A");
    }

    private static string MiniMaxH3CameraMovement(string value) => value switch
    {
        "locked" => "a static shot with the camera position and lens held still",
        "pull_back" => "a pull-out with small amplitude at slow speed",
        "pan_left" => "a pan left with small amplitude at slow speed",
        "pan_right" => "a pan right with small amplitude at slow speed",
        "orbit_left" => "an arc to the left with small amplitude at slow speed",
        "orbit_right" => "an arc to the right with small amplitude at slow speed",
        "gentle_tracking" => "a tracking movement with small amplitude at slow speed",
        "subtle_handheld" => "a handheld movement with slight shake",
        _ => "a push-in with small amplitude at slow speed"
    };

    private static AiVideoTemplateDefinition WithRequiredGuardrail(AiVideoTemplateDefinition template)
    {
        var defaults = template.Code switch
        {
            "macro_detail_reveal" => template.Defaults with { CameraMovement = "locked", SubjectMotion = "none", MotionIntensity = "subtle" },
            "cinematic_product_showcase" => template.Defaults with { SubjectMotion = "none" },
            _ => template.Defaults
        };
        var commonPrompt = template.Code == "lifestyle_in_use"
            ? ProductOnModelGuardrail
            : EffectiveCommonPrompt(template.CommonPrompt);
        var subtemplates = template.Code == "lifestyle_in_use"
            ? template.Subtemplates?.Select(item => item.Code == "profile_tracking" &&
                item.BasePrompt == BuiltIns["lifestyle_in_use"].Subtemplates!.Single(builtIn => builtIn.Code == "profile_tracking").BasePrompt
                    ? item with { Name = "Profile Tracking", Description = "A slow, smooth, shallow camera arc to frame-right, stopping before the view passes behind the model." }
                    : item).ToArray()
            : template.Subtemplates;
        return template with { CommonPrompt = commonPrompt, Defaults = defaults, Subtemplates = subtemplates };
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

    private static string ProductContextPrompt(string templateCode, string productType, AiVideoProductContext context)
    {
        if (templateCode == "lifestyle_in_use")
            return $"Product: {productType}. Match the approved image for its visible design, colors, fit and proportions.";

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

    private static string NaturalProductMotion(string productType) => productType switch
    {
        "T-shirt" or "hoodie" => "The garment moves naturally with the body, forming small folds where the fabric bends.",
        "tote bag" => "The bag and any visible straps move naturally while maintaining their established relationship to the model.",
        "mug" or "poster" or "phone case" => "The product keeps its rigid shape and its established position relative to the person or surroundings.",
        _ => "The product moves naturally with the scene while preserving its shape and established position."
    };

    private static string DetailSubjectMotion(string productType) => productType switch
    {
        "T-shirt" or "hoodie" => "Gentle breathing causes small movement in the visible torso and existing fabric folds.",
        "tote bag" => "The model breathes gently; any small bag or strap movement follows its existing contact with the model.",
        "mug" or "poster" or "phone case" => "The model breathes gently while the product keeps its rigid shape and established placement.",
        _ => "The model breathes gently while the product retains its existing shape and placement."
    };

    public static AiVideoSceneOptions ApplyDefaults(AiVideoTemplateDefinition template, AiVideoSceneOptions options, string? modelKey = null)
    {
        string Value(string key, string value, string fallback) =>
            template.AllowedSettings.TryGetValue(key, out var allowed) && allowed.Contains(value, StringComparer.Ordinal)
                ? value
                : fallback;
        var defaults = ResolveDefaults(template, options.SubtemplateCode);
        var subtemplate = ResolveSubtemplate(template, options.SubtemplateCode);
        var subtemplateCode = subtemplate?.Code;
        if (AiVideoModel.UsesStructuredPrompt(modelKey) && !AiVideoPromptRules.IsRequested(options) &&
            template.Code == "lifestyle_in_use" && subtemplate is { Code: "natural_motion" or "profile_tracking" } &&
            subtemplate.BasePrompt == BuiltIns["lifestyle_in_use"].Subtemplates!
                .Single(item => item.Code == subtemplate.Code).BasePrompt)
        {
            // These are effective controls, not just wording overrides: snapshot and prompt agree.
            // P1 retains its existing normalized template controls for isolated action-plan tests.
            defaults = defaults with { CameraAngle = "source_view", Lighting = "source_image" };
        }
        if (!AdvancedControlsEnabled)
            return options with
            {
                TemplateCode = template.Code,
                SubtemplateCode = subtemplateCode,
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
            SubtemplateCode = subtemplateCode,
            Framing = Value("framing", options.Framing, defaults.Framing),
            CameraAngle = Value("cameraAngle", options.CameraAngle, defaults.CameraAngle),
            CameraMovement = Value("cameraMovement", options.CameraMovement, defaults.CameraMovement),
            SubjectMotion = Value("subjectMotion", options.SubjectMotion, defaults.SubjectMotion),
            MotionIntensity = Value("motionIntensity", options.MotionIntensity, defaults.MotionIntensity),
            Lighting = Value("lighting", options.Lighting, defaults.Lighting),
            ExtraInstructions = string.IsNullOrWhiteSpace(options.ExtraInstructions) ? null : options.ExtraInstructions.Trim()
        };
    }

    internal static AiVideoSceneOptions ResolveDefaults(AiVideoTemplateDefinition template, string? requestedCode) =>
        ResolveSubtemplate(template, requestedCode)?.Defaults ?? template.Defaults;

    internal static AiVideoSubtemplateDefinition? ResolveSubtemplate(AiVideoTemplateDefinition template, string? requestedCode)
    {
        if (template.Subtemplates is not { Count: > 0 }) return null;
        var code = string.IsNullOrWhiteSpace(requestedCode) ? template.DefaultSubtemplateCode : requestedCode;
        return template.Subtemplates.FirstOrDefault(item => item.Code == code)
            ?? template.Subtemplates.FirstOrDefault(item => item.Code == template.DefaultSubtemplateCode)
            ?? template.Subtemplates[0];
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

        const string naturalMotionPrompt = "Create one continuous {{DURATION}}-second live-action product-on-model fashion advertisement from the approved source image. {{PRODUCT_CONTEXT}} {{NATURAL_MOTION}} The camera performs {{CAMERA_MOVEMENT}} at {{CAMERA_ANGLE}}, starting immediately and continuing in a steady, uninterrupted glide throughout the shot. Begin in {{FRAMING}} and keep the change in framing small; keep the model's head and product artwork in frame when visible in the opening image. {{LIGHTING}} Preserve the source setting and natural shadows.";
        const string fabricDetailPrompt = "Create one continuous {{DURATION}}-second live-action editorial product-detail advertisement using the approved source image as the opening frame. {{PRODUCT_CONTEXT}} Follow the same visible product detail already emphasized by the opening crop throughout the shot. Preserve that region's existing texture, construction and any visible print edges or lettering. Do not switch to a different detail or add features absent from the source. {{DETAIL_MOTION}} From the first frame, the camera performs {{CAMERA_MOVEMENT}}, starting from {{CAMERA_ANGLE}}. Keep the movement stabilized with small amplitude at slow speed, gentle starts and stops, and approximately constant height, distance, focal length and detail scale. Keep the lens aimed at the same product region, allowing natural perspective and restrained background parallax. Shorten any camera arc when the crop is tight and stop before passing behind the shoulder or revealing the back. Keep focus on the same product surface throughout, without a focus pull, zoom or sudden reframing. Preserve the background and setting shown in the source image. {{LIGHTING}}";
        const string brandFocusPrompt = "Create one continuous {{DURATION}}-second live-action brand-focused product-on-model fashion advertisement using the approved source image as the opening frame. {{PRODUCT_CONTEXT}} Keep the product's existing artwork, lettering or logo as the visual focus. If no branding is visible, emphasize the existing silhouette, colors and construction instead. Keep the design area visible in the opening image inside the frame and in focus throughout. Do not invent branding or reveal design areas absent from the source. {{BRAND_MOTION}} From the first frame, the camera performs {{CAMERA_MOVEMENT}}, continuing in a steady, uninterrupted glide. Keep the approach shallow so the visible design stays within the frame. Maintain {{CAMERA_ANGLE}}, without orbiting around the model or shifting focus away from the product. Preserve the background and setting shown in the source image. {{LIGHTING}}";
        const string profileTrackingPrompt = "Create one continuous {{DURATION}}-second live-action product-on-model fashion advertisement using the approved source image as the opening frame. {{PRODUCT_CONTEXT}} {{PROFILE_MOTION}} From the first frame, the camera moves around the model toward the right-hand side of the opening image along a short, shallow arc, at a steady, measured pace, with clearly visible progress throughout the shot. Begin moving immediately and continue in one smooth, uninterrupted glide, without pausing, speeding up or slowing down. Keep approximately the same camera height and distance. Keep the lens aimed at the upper torso and the product in focus as the viewing angle changes. Maintain approximately the same framing throughout the arc. Preserve the background and setting shown in the source image. Any visible design remains unchanged and attached to the same area of the garment. As the camera moves around the model, view the design from the changing camera angle. Do not redraw, stretch or rearrange the design. {{LIGHTING}}";
        const string naturalMotionStartFramePrompt = "Create a photorealistic medium eye-level fashion keyframe. The model faces mostly forward with a slight three-quarter turn, feet planted, relaxed shoulders and natural arms that do not cover the garment or artwork. Keep the full torso, product silhouette and visible design clear in warm editorial light.";
        const string fabricDetailStartFramePrompt = "Create a photorealistic close editorial keyframe of one real product detail visible in the source image, retaining its relationship to the model. Keep existing texture, construction, print edges, lettering and colors clear. Preserve the source viewing direction, lighting and setting. Do not invent stitching, artwork or material features.";
        const string brandFocusStartFramePrompt = "Create a photorealistic medium-close product-on-model keyframe using the source image as the reference. Frame the existing visible artwork, lettering or logo clearly; if none is visible, emphasize the product's real silhouette and colors. Keep hands from covering the design where the source pose allows. Preserve the source viewing direction, lighting, setting and natural product shape. Do not add branding or complete hidden design areas.";
        const string profileTrackingStartFramePrompt = "Create a photorealistic fashion keyframe using the source image as the reference. The model stands comfortably, facing mostly forward with relaxed shoulders and arms away from the garment design. Keep the visible garment front and artwork clear, with space around the model for a short camera arc. Preserve the source setting and lighting. No walking pose.";
        var naturalMotionDefaults = new AiVideoSceneOptions(TemplateCode: "lifestyle_in_use", SubtemplateCode: "natural_motion", DurationSeconds: 4, Framing: "medium", CameraAngle: "eye_level", CameraMovement: "push_in", SubjectMotion: "minimal", MotionIntensity: "subtle", Lighting: "warm_natural");
        var fabricDetailDefaults = new AiVideoSceneOptions(TemplateCode: "lifestyle_in_use", SubtemplateCode: "fabric_detail", DurationSeconds: 4, Framing: "close_up", CameraAngle: "source_view", CameraMovement: "orbit_right", SubjectMotion: "minimal", MotionIntensity: "subtle", Lighting: "source_image");
        var brandFocusDefaults = new AiVideoSceneOptions(TemplateCode: "lifestyle_in_use", SubtemplateCode: "brand_focus", DurationSeconds: 4, Framing: "medium", CameraAngle: "source_view", CameraMovement: "push_in", SubjectMotion: "minimal", MotionIntensity: "subtle", Lighting: "source_image");
        var profileTrackingDefaults = new AiVideoSceneOptions(TemplateCode: "lifestyle_in_use", SubtemplateCode: "profile_tracking", DurationSeconds: 4, Framing: "medium", CameraAngle: "three_quarter", CameraMovement: "orbit_right", SubjectMotion: "minimal", MotionIntensity: "subtle", Lighting: "source_image");
        var lifestyle = new AiVideoTemplateDefinition("lifestyle_in_use", "Product On Model",
            "Fashion videos that keep the exact product naturally presented on the model.", 12,
            naturalMotionPrompt,
            true, false, Settings(["wide", "medium", "medium_close", "close_up"], ["eye_level", "three_quarter", "source_view"], ["locked", "push_in", "gentle_tracking", "orbit_right"], ["minimal", "natural"], ["warm_natural", "golden_hour", "bright_clean", "source_image"]),
            naturalMotionDefaults,
            Subtemplates:
            [
                new("natural_motion", "Natural Motion", "A balanced fashion shot with restrained model movement and a slow camera push.", naturalMotionPrompt, naturalMotionDefaults, naturalMotionStartFramePrompt),
                new("fabric_detail", "Fabric Detail Close-Up", "A slow, small camera arc to frame-right around one existing product detail, keeping its scale and focus stable.", fabricDetailPrompt, fabricDetailDefaults, fabricDetailStartFramePrompt),
                new("brand_focus", "Brand Focus", "A shallow camera approach that keeps the existing visible design or product silhouette in focus.", brandFocusPrompt, brandFocusDefaults, brandFocusStartFramePrompt),
                new("profile_tracking", "Profile Tracking", "A slow, smooth, shallow camera arc to frame-right, stopping before the view passes behind the model.", profileTrackingPrompt, profileTrackingDefaults, profileTrackingStartFramePrompt)
            ],
            DefaultSubtemplateCode: "natural_motion");

        var ugc = new AiVideoTemplateDefinition("creator_pov_ugc", "Creator POV / UGC",
            "An authentic creator-style product demonstration with a phone-camera feel.", 3,
            "Create an authentic creator-style product demonstration featuring the person already visible in the approved source image. Use a realistic phone-camera aesthetic in {{FRAMING}}, {{CAMERA_ANGLE}} composition. The camera uses {{CAMERA_MOVEMENT}} with controlled micro-movement. The creator naturally presents the exact product toward the camera and avoids deliberately hiding its main design; natural hand overlap, folds, and perspective are allowed when product identity remains stable. {{LIGHTING}} Maintain candid UGC realism, accurate anatomy and stable product details. There is no dialogue, lip-sync or exaggerated performance. Create a {{DURATION}}-second shot.",
            true, false, Settings(["medium_close", "close_up"], ["eye_level", "three_quarter"], ["locked", "push_in", "subtle_handheld"], ["minimal", "natural"], ["bright_clean", "warm_natural"]),
            new(TemplateCode: "creator_pov_ugc", DurationSeconds: 4, Framing: "close_up", CameraAngle: "eye_level", CameraMovement: "subtle_handheld", SubjectMotion: "natural", MotionIntensity: "subtle", Lighting: "bright_clean"));

        var story = new AiVideoTemplateDefinition("everyday_story_moment", "Everyday Story Moment",
            "A warm, grounded everyday scene centered on the product.", 3,
            "Create a warm, emotionally grounded everyday moment centered on the person or people already visible in the approved source image and the exact product. The person performs {{PRODUCT_ACTION}} while the camera observes with {{CAMERA_MOVEMENT}} from {{CAMERA_ANGLE}}, using {{FRAMING}}. {{LIGHTING}} Use a natural expression, restrained movement and intimate commercial storytelling. Keep product identity stable and recognizable whenever it is naturally visible; do not require artificial full visibility in every frame. Create a {{DURATION}}-second shot.",
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
        "source_view" => "the viewing direction established by the opening image",
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
        "source_image" => "Preserve the lighting shown in the source image.",
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
