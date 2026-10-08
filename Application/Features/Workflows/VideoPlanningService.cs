using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows.Validators;
using APCS.Common.Models;
using APCS.Domain.Entities;

namespace APCS.Application.Features.Workflows;

public sealed record PreviewStoryboardRequest(GenerateVideoConfig Config, IReadOnlyList<Guid> MockupIds, string? ArtworkGroupKey);
public interface IVideoPlanningService
{
    Task<Result<Storyboard>> PreviewAsync(Guid productId, PreviewStoryboardRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<VideoTemplateCatalogItem>>> ListTemplatesAsync(CancellationToken ct);
}
public sealed class VideoPlanningService(ICurrentUser user, IRepository<Product> products, IRepository<MockupImage> assets,
    IRepository<VideoTemplate> templates, GenerateVideoConfigValidator validator, WorkflowCapabilityRegistry capabilities,
    VideoStoryboardPlanner planner) : IVideoPlanningService
{
    public async Task<Result<Storyboard>> PreviewAsync(Guid productId, PreviewStoryboardRequest request, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(productId, ct);
        if (product is null || product.UserId != user.UserId || product.DeletedAt != null) return Result.Failure<Storyboard>(Error.NotFound("ProductNotFound", "Product not found."));
        if (request.Config is null || request.MockupIds is null) return Result.Failure<Storyboard>(Error.Validation("Video configuration and mockup selection are required."));
        if (!capabilities.IsModeEnabled(request.Config.Mode)) return Result.Failure<Storyboard>(new("UnsupportedVideoMode", "Only Standard Showcase is enabled.", ErrorType.Validation));
        var validation = await validator.ValidateAsync(request.Config, ct);
        if (!validation.IsValid || request.MockupIds.Count > 8) return Result.Failure<Storyboard>(Error.Validation("Invalid video configuration or mockup selection."));
        var approved = await assets.FindAsync(x => x.ProductId == productId && x.DeletedAt == null && x.ApprovalStatus == "approved" && x.ApprovedRevision == x.MetadataRevision, ct);
        return planner.Plan(product, approved.Where(x => request.MockupIds.Count == 0 || request.MockupIds.Contains(x.Id))
            .Where(x => string.IsNullOrEmpty(request.ArtworkGroupKey) || x.ArtworkGroupKey == request.ArtworkGroupKey).Take(8).ToArray(), request.Config);
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
                effects.DefaultTransition ?? "fade",
                effects.DefaultMotionPreset ?? "varied",
                effects.Requirements ?? FallbackRequirements(x.Code));
        }).ToArray();
        return Result.Success<IReadOnlyList<VideoTemplateCatalogItem>>(result);
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

    private static VideoTemplateRequirements FallbackRequirements(string? code) => code switch
    {
        "design_detail" => new(1, true, 0),
        "variant_showcase" => new(2, false, 2),
        _ => new(1, false, 0),
    };

    private sealed record VideoTemplateEffects(string? Description, string? DefaultTransition,
        string? DefaultMotionPreset, VideoTemplateRequirements? Requirements);
}
