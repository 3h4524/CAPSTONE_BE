using FluentValidation;
using System.Text.Json;

namespace APCS.Application.Features.Workflows.Validators;

public sealed class GenerateVideoConfigValidator : AbstractValidator<GenerateVideoConfig>
{
    public GenerateVideoConfigValidator()
    {
        RuleFor(x => x.Mode).Must(x => x is "standard" or "ai_shot");
        RuleFor(x => x.Target).Equal("etsy");
        RuleFor(x => x.Template).Must(x => new[] { "auto", "product_showcase", "design_detail", "variant_showcase" }.Contains(x));
        RuleFor(x => x).Must(x => x.Mode != "standard" || x.Template != "variant_showcase")
            .WithMessage("Standard video supports Product Showcase and Design Detail templates.");
        RuleFor(x => x.TemplateVersion).Must(x => x is null or 1 or 2);
        RuleFor(x => x.OutputFormat).Must(VideoOutputFormats.IsSupported).WithMessage("Choose a supported video output format.");
        RuleFor(x => x).Must(x => x.Mode != "ai_shot" || x.OutputFormat != "square" ||
            x.AiScenes is { Count: > 0 } && x.AiScenes.All(scene => scene.Mode == "standard"))
            .WithMessage("AI video supports portrait or landscape output.");
        RuleFor(x => x.AiStudioMode).Must(x => x is "direct" or "studio").WithMessage("Choose direct mock-up or a studio keyframe.");
        RuleFor(x => x.AiStudioLayout).Must(AiStudioFrameIdentity.IsSupportedLayout).WithMessage("Choose a supported studio layout.");
        RuleFor(x => x.DurationSeconds).InclusiveBetween(3, 30);
        RuleFor(x => x.AssetSelection).Must(x => x is "automatic" or "manual");
        RuleFor(x => x.SelectedMockupIds).Must(x => x is null || x.Count <= 8 && x.Distinct().Count() == x.Count);
        RuleFor(x => x.SceneOrder).Must(x => x is null || x.Count <= 4 && x.Distinct().Count() == x.Count);
        RuleFor(x => x.StandardScenes).Must(x => x is null || x.Count <= 4 && x.All(scene => scene.MockupId != Guid.Empty) && x.Select(scene => scene.MockupId).Distinct().Count() == x.Count)
            .WithMessage("Configure up to four distinct Standard scenes.");
        RuleFor(x => x.StandardScenes).Must(x => x is null || x.All(scene => ValidCrop(scene.BaseCrop) && ValidCrop(scene.StartCrop) && ValidCrop(scene.EndCrop)))
            .WithMessage("Scene framing must stay inside the source image.");
        RuleFor(x => x.TextOverlay).NotNull().MaximumLength(120);
        RuleFor(x => x.AiShotOptions).NotNull().When(x => x.Mode == "ai_shot" && x.AiScenes is not { Count: > 0 });
        RuleFor(x => x.AiScenes).Must(x => x is null || x.Count is >= 1 and <= 4)
            .WithMessage("Configure between one and four scenes.");
        RuleFor(x => x.AiScenes).Must(x => x is null || x.All(scene => scene.Mode is "ai" or "standard"))
            .WithMessage("Choose AI video or Standard motion for every scene.");
        RuleFor(x => x.AiScenes).Must(x => x is null || x.All(scene => scene.MockupId is Guid id && id != Guid.Empty))
            .When(x => x.Mode == "ai_shot" && x.AiScenes is { Count: > 0 })
            .WithMessage("Choose an approved product image or keyframe for every scene.");
        RuleFor(x => x.AiScenes).Must(x => x is null || x.All(scene => scene.DurationSeconds is 4 or 6 or 8) && x.Sum(scene => scene.DurationSeconds) <= 30)
            .WithMessage("Scenes must be 4, 6 or 8 seconds each, with a maximum combined duration of 30 seconds.");
        RuleFor(x => x.AiScenes).Must(x => x is null || x.All(scene => scene.Mode != "standard" ||
            (scene.StandardMotionPreset is null or "gentle" or "contain_gentle" or "static" or "pan" or "zoom" or "zoom_out" or "pan_left" or "pan_up" or "pan_down" or
                "diagonal_up_right" or "diagonal_up_left" or "diagonal_down_right" or "diagonal_down_left" or "custom") &&
            ValidCrop(scene.StandardBaseCrop) && ValidCrop(scene.StandardStartCrop) && ValidCrop(scene.StandardEndCrop)))
            .WithMessage("Standard camera movement and framing must stay inside the source image.");
        RuleFor(x => x.AiScenes).Must(x => x is null || x.All(scene => scene.ExtraInstructions is null or { Length: <= 500 }))
            .WithMessage("Keep each scene's extra instructions under 500 characters.");
        RuleFor(x => x.AiGlobalInstructions).Must(x => x is null or { Length: <= 500 })
            .WithMessage("Keep shared AI scene directions under 500 characters.");
        RuleFor(x => x).Must(x => x.Mode != "ai_shot" || x.AiScenes is not { Count: > 0 } || x.DurationSeconds == x.AiScenes.Sum(scene => scene.DurationSeconds))
            .WithMessage("Overall video duration must equal the sum of AI scene durations.");
        RuleFor(x => x).Must(x => x.Mode != "ai_shot" || x.AiScenes is not { Count: > 0 } || x.AiShotOptions == null)
            .WithMessage("Use either the scene-based AI settings or legacy AI Shot settings, not both.");
        RuleFor(x => x).Must(x => x.AssetSelection != "manual" || x.SelectedMockupIds is { Count: > 0 }).WithMessage("Select mockups for manual asset selection.");
        RuleFor(x => x.StandardOptions).Must(x => x is null ||
            new[] { "varied", "gentle", "contain_gentle", "static", "pan", "zoom", "zoom_out", "pan_left", "pan_up", "pan_down",
                "diagonal_up_right", "diagonal_up_left", "diagonal_down_right", "diagonal_down_left" }.Contains(x.MotionPreset) && x.Crop == "safe" &&
            new[] { "none", "fade", "swipe", "zoom", "flash", "whip_pan", "blur", "cut" }.Contains(x.Transition));
        RuleFor(x => x.SceneMotionPresets).Must(x => x is null || x.Count <= 4 && x.All(m => m is null ||
            new[] { "gentle", "contain_gentle", "static", "pan", "zoom", "zoom_out", "pan_left", "pan_up", "pan_down",
                "diagonal_up_right", "diagonal_up_left", "diagonal_down_right", "diagonal_down_left", "custom" }.Contains(m)));
        RuleFor(x => x.AiShotOptions).Must(x => x is null ||
            x.Coverage is "single_scene" or "full_video" &&
            x.Concept is "product_cinematic" or "material_detail" or "lifestyle_motion" or "product_in_use" &&
            x.SceneIndex is >= 0 and <= 3 &&
            x.Prompt is null or { Length: <= 500 } &&
            (x.FullSceneMockupIds is null || x.FullSceneMockupIds.Count <= 2 && x.FullSceneMockupIds.All(id => id != Guid.Empty)));
        RuleFor(x => x).Must(x => x.Mode != "ai_shot" || x.AiScenes is { Count: > 0 } || x.AiShotOptions?.Coverage != "full_video" || x.DurationSeconds is 8 or 15)
            .WithMessage("Full AI videos must be 8 or 15 seconds.");
        RuleFor(x => x).Must(x => x.Mode != "ai_shot" || x.AiScenes is { Count: > 0 } || x.AiShotOptions?.Concept is not ("lifestyle_motion" or "product_in_use") || x.AiShotOptions.RightsConfirmed)
            .WithMessage("Confirm rights to use the adult Lifestyle image for AI video generation.");
        RuleFor(x => x).Must(x => x.Mode != "ai_shot" || x.AiScenes is { Count: > 0 } || x.AiShotOptions?.Coverage != "single_scene" || x.DurationSeconds is >= 12 and <= 15)
            .WithMessage("A mixed AI Shot video must be 12–15 seconds.");
    }

    private static bool ValidCrop(Region? region) => region is null ||
        double.IsFinite(region.X + region.Y + region.Width + region.Height) && region.X >= 0 && region.Y >= 0 &&
        region.Width > 0 && region.Height > 0 && region.X + region.Width <= 1 && region.Y + region.Height <= 1;
}

public sealed class SaveWorkflowValidator : AbstractValidator<SaveWorkflowRequest>
{
    public SaveWorkflowValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).NotNull().MaximumLength(500);
        RuleFor(x => x.Definition).NotNull();
        When(x => x.Definition != null, () =>
        {
            RuleFor(x => x.Definition.Version).Equal(2);
            RuleFor(x => x.Definition.Nodes).NotNull().Must(x => x is { Count: > 0 and <= 40 });
            RuleFor(x => x.Definition.Edges).NotNull().Must(x => x is { Count: <= 80 });
            RuleFor(x => x.Definition).Must(WithinLimit).WithMessage("Definition is invalid or too large.");
            RuleFor(x => x.Definition.Nodes).Must(x => x == null || x.All(n => n != null && n.Position != null && double.IsFinite(n.Position.X) && double.IsFinite(n.Position.Y) && SafeConfig(n.Config))).WithMessage("Node configuration cannot contain credentials or provider routing settings.");
        });
    }
    private static bool WithinLimit(WorkflowDefinition definition)
    {
        try { return WorkflowJson.Write(definition).Length <= 100_000; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException) { return false; }
    }
    private static bool SafeConfig(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().All(p => !new[] { "apikey", "apisecret", "secret", "token", "accesstoken", "password", "provider", "providername", "connectionstring", "signedurl" }.Contains(p.Name.Replace("_", "").Replace("-", "").ToLowerInvariant()) && SafeConfig(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().All(SafeConfig),
        JsonValueKind.Undefined => false,
        _ => true
    };
}

public sealed class MockupMetadataValidator : AbstractValidator<MockupMetadataRequest>
{
    public MockupMetadataValidator()
    {
        RuleFor(x => x.ExpectedRevision).GreaterThan(0);
        RuleFor(x => x.Role).Must(x => new[] { "Hero", "ArtworkDetail", "AlternativeAngle", "Lifestyle", "Variant" }.Contains(x));
        RuleFor(x => x.ArtworkGroupKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.VariantKey).MaximumLength(100);
        RuleFor(x => x.Regions).NotNull();
        RuleFor(x => x.Regions).Must(x => x != null && Valid(x.Product) && Valid(x.Artwork) && Valid(x.Detail) &&
            (x.FocalPoint == null || double.IsFinite(x.FocalPoint.X) && double.IsFinite(x.FocalPoint.Y) &&
             x.FocalPoint.X is >= 0 and <= 1 && x.FocalPoint.Y is >= 0 and <= 1));
    }
    private static bool Valid(Region? r) => r is null || double.IsFinite(r.X + r.Y + r.Width + r.Height) &&
        r.X >= 0 && r.Y >= 0 && r.Width > 0 && r.Height > 0 && r.X + r.Width <= 1 && r.Y + r.Height <= 1;
}
