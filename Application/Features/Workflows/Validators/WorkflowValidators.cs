using FluentValidation;
using System.Text.Json;

namespace APCS.Application.Features.Workflows.Validators;

public sealed class GenerateVideoConfigValidator : AbstractValidator<GenerateVideoConfig>
{
    public GenerateVideoConfigValidator()
    {
        RuleFor(x => x.Mode).Must(x => new[] { "standard", "ai_background", "ai_shot" }.Contains(x));
        RuleFor(x => x.Target).Equal("etsy");
        RuleFor(x => x.Template).Must(x => new[] { "auto", "product_showcase", "design_detail", "variant_showcase" }.Contains(x));
        RuleFor(x => x.TemplateVersion).Must(x => x is null or 1 or 2);
        RuleFor(x => x.OutputFormat).Must(VideoOutputFormats.IsSupported).WithMessage("Choose a supported video output format.");
        RuleFor(x => x.DurationSeconds).InclusiveBetween(3, 15);
        RuleFor(x => x.AssetSelection).Must(x => x is "automatic" or "manual");
        RuleFor(x => x.SelectedMockupIds).Must(x => x is null || x.Count <= 8 && x.Distinct().Count() == x.Count);
        RuleFor(x => x.SceneOrder).Must(x => x is null || x.Count <= 4 && x.Distinct().Count() == x.Count);
        RuleFor(x => x.TextOverlay).NotNull().MaximumLength(120);
        RuleFor(x => x.AiBackgroundOptions).Null().When(x => x.Mode != "ai_background");
        RuleFor(x => x.AiShotOptions).Null().When(x => x.Mode != "ai_shot");
        RuleFor(x => x).Must(x => x.AssetSelection != "manual" || x.SelectedMockupIds is { Count: > 0 }).WithMessage("Select mockups for manual asset selection.");
        RuleFor(x => x.StandardOptions).Must(x => x is null ||
            new[] { "varied", "gentle", "contain_gentle", "static", "pan", "zoom", "zoom_out", "pan_left", "pan_up", "pan_down",
                "diagonal_up_right", "diagonal_up_left", "diagonal_down_right", "diagonal_down_left" }.Contains(x.MotionPreset) && x.Crop == "safe" &&
            new[] { "fade", "cut" }.Contains(x.Transition));
        RuleFor(x => x.SceneMotionPresets).Must(x => x is null || x.Count <= 4 && x.All(m => m is null ||
            new[] { "gentle", "contain_gentle", "static", "pan", "zoom", "zoom_out", "pan_left", "pan_up", "pan_down",
                "diagonal_up_right", "diagonal_up_left", "diagonal_down_right", "diagonal_down_left" }.Contains(m)));
        RuleFor(x => x.AiShotOptions).Must(x => x is null || x.SceneIndex >= 0 && x.SceneIndex <= 3 && x.MaximumAttempts is >= 1 and <= 2);
    }
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
