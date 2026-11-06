using System.Text.RegularExpressions;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Workflows;

/// <summary>Puts an existing mock-up's design, in its garment color, on a model photo.</summary>
/// <param name="GarmentMockupId">The composited mock-up to copy.</param>
/// <param name="ModelPhotoId">The model photo (a mock-up template of the 'model' kind).</param>
/// <param name="GarmentColor">'#RRGGBB' for the garment; null takes the mock-up's own.</param>
public sealed record ModelPhotoMockupRequest(Guid GarmentMockupId, Guid ModelPhotoId, string? GarmentColor = null);

/// <summary>How the mock-up would look on the model photo, before anything is stored.</summary>
/// <param name="Url">The composite's delivery URL; the CDN renders it when it is first asked for.</param>
public sealed record ModelPhotoPreviewResponse(string Url);

public interface IModelPhotoMockupService
{
    Task<Result<MockupResponse>> GenerateAsync(Guid productId, ModelPhotoMockupRequest request, CancellationToken ct);

    /// <summary>The image <see cref="GenerateAsync"/> would make, as a URL to look at; nothing is stored.</summary>
    Task<Result<ModelPhotoPreviewResponse>> PreviewAsync(Guid productId, ModelPhotoMockupRequest request, CancellationToken ct);
}

/// <summary>
/// Dresses the person on a model photo in the product of an existing mock-up without generative AI: the person's
/// garment is recolored to the mock-up's garment color and the mock-up's design is printed on it where it sits on
/// the mock-up, bent by the fabric's folds, by the same compositing every mock-up uses.
/// </summary>
/// <remarks>
/// The result is stored like the AI lifestyle image (a Lifestyle mock-up in the design's artwork group), as a
/// snapshot so a video can use it. It stays pending until the seller approves it at the Mockup Approval gate.
/// </remarks>
public sealed class ModelPhotoMockupService(ICurrentUser user, IRepository<Product> products, IRepository<MockupImage> assets,
    IRepository<MockupTemplate> templates, IRepository<DesignImage> designs, IRepository<BatchJobProduct> rows,
    IRepository<BatchJob> batches, IMockupCompositor compositor, IMockupMapService maps, IMediaStorage storage, IMockupAssetService mockups,
    IUnitOfWork uow, TimeProvider time, ILogger<ModelPhotoMockupService> logger) : IModelPhotoMockupService
{
    public const string Role = "Lifestyle";

    public async Task<Result<ModelPhotoPreviewResponse>> PreviewAsync(Guid productId, ModelPhotoMockupRequest request, CancellationToken ct)
    {
        var composed = await ComposeAsync(productId, request, ct);
        return composed.IsFailure
            ? Result.Failure<ModelPhotoPreviewResponse>(composed.Error)
            : Result.Success(new ModelPhotoPreviewResponse(composed.Value.Url));
    }

    public async Task<Result<MockupResponse>> GenerateAsync(Guid productId, ModelPhotoMockupRequest request, CancellationToken ct)
    {
        var composed = await ComposeAsync(productId, request, ct);
        if (composed.IsFailure)
            return Result.Failure<MockupResponse>(composed.Error);
        var (url, userId, garment, design, model, color) = composed.Value;

        // Made exactly as before: that image is the answer, not a second copy beside it.
        var same = (await assets.FindAsync(x => x.ProductId == productId && x.DeletedAt == null && x.Role == Role
            && x.MockupImageUrl == url && x.ContentHash != null, ct)).FirstOrDefault();
        if (same is not null)
            return await ListedAsync(productId, same.Id, ct);

        var id = Guid.NewGuid();
        StoredMockup saved;
        try
        {
            saved = await storage.UploadMockupFromUrlAsync(url, $"mockups/{userId}/{productId}/{id}", ct);
        }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException)
        {
            logger.LogWarning(error, "Could not render mock-up {MockupId} on model photo {ModelPhotoId}", garment.Id, model.Id);
            return Result.Failure<MockupResponse>(Error.Failure("ModelPhotoRenderFailed", "Could not render the model photo. Try again."));
        }

        var now = time.GetUtcNow().UtcDateTime;
        await assets.AddAsync(new MockupImage
        {
            Id = id, ProductId = productId, DesignImageId = design.Id, MockupTemplateId = model.Id, BatchJobProductId = garment.BatchJobProductId,
            SourceType = MockupSources.Generated, StorageProvider = "cloudinary", StorageKey = saved.StorageKey, StorageVersion = saved.Version,
            ContentHash = saved.Hash, MockupImageUrl = url, MockupWidthPx = saved.Width, MockupHeightPx = saved.Height, GenerationTimeSeconds = 0,
            GarmentColor = color, Role = Role, ArtworkGroupKey = garment.ArtworkGroupKey ?? MockupSources.ArtworkGroupKey(design.Id), VariantKey = color,
            MetadataRevision = MockupSources.FirstRevision, ApprovalStatus = "pending", Regions = WorkflowJson.Write(new MockupRegions()),
            IsFinal = false, CreatedAt = now
        }, cancellationToken: ct);
        model.UsageCount = (model.UsageCount ?? 0) + 1;
        model.UpdatedAt = now;
        await templates.UpdateAsync(model, cancellationToken: ct);
        await uow.SaveChangesAsync(ct);

        return await ListedAsync(productId, id, ct);
    }

    private sealed record Composed(string Url, Guid UserId, MockupImage Garment, DesignImage Design, MockupTemplate Model, string Color);

    /// <summary>The composite of the mock-up's design on the model photo, after every check; nothing is stored.</summary>
    private async Task<Result<Composed>> ComposeAsync(Guid productId, ModelPhotoMockupRequest request, CancellationToken ct)
    {
        if (user.UserId is not Guid userId || await products.GetByIdAsync(productId, ct) is not { DeletedAt: null } product || product.UserId != userId)
            return Result.Failure<Composed>(Missing());
        if (request.GarmentColor is { } requested && !Regex.IsMatch(requested, MockupRules.GarmentColorPattern))
            return Result.Failure<Composed>(Error.Validation("Garment color must look like #1F2A44."));

        // Only a mock-up composited here knows its design, template and print area; an uploaded photo does not.
        var garment = await assets.GetByIdAsync(request.GarmentMockupId, ct);
        if (garment is null || garment.DeletedAt != null || garment.ProductId != productId)
            return Result.Failure<Composed>(Missing());
        if (garment.SourceType != MockupSources.Generated || garment.Role == Role
            || garment.DesignImageId is not Guid designId || garment.MockupTemplateId is not Guid garmentTemplateId)
            return Result.Failure<Composed>(Error.Validation("Choose a mock-up made from a design on a mock-up template."));

        var design = await designs.GetByIdAsync(designId, ct);
        var garmentTemplate = await templates.GetByIdAsync(garmentTemplateId, ct);
        if (design is null || design.DeletedAt != null || garmentTemplate is null
            || MockupRules.ParsePrintArea(garmentTemplate.PrintAreaConfig) is not { } templateArea)
            return Result.Failure<Composed>(Error.Conflict("MockupNotReusable", "This mock-up's design or template is no longer available. Choose another mock-up."));

        var model = await templates.Query().SingleOrDefaultAsync(item => item.Id == request.ModelPhotoId && item.IsActive == true
            && item.Kind == MockupRules.ModelKind && (item.UserId == null || item.UserId == userId), ct);
        if (model is null)
            return Result.Failure<Composed>(MockupErrors.ModelPhotoNotFound());
        if (!string.Equals(model.ProductType, product.ProductType, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<Composed>(MockupErrors.TemplateNotForProduct(model.Name));
        if (!MockupRules.HasCurrentMaps(model) || !model.AllowRecolor || MockupRules.ParsePrintArea(model.PrintAreaConfig) is not { } modelArea)
            return Result.Failure<Composed>(MockupErrors.TemplateNotUsable(model.Name));

        // The model wears the mock-up's garment color. A mock-up left in its photo's own color names none; that
        // color is then read from the photo itself.
        var color = (request.GarmentColor ?? garment.GarmentColor ?? garmentTemplate.GarmentColor
            ?? await PhotoGarmentColorAsync(garmentTemplate, ct))?.ToUpperInvariant();
        if (color is null)
            return Result.Failure<Composed>(Error.Validation("The garment color of this mock-up could not be read from its photo. Choose another mock-up."));

        var used = await UsedPrintAreaAsync(garment, garmentTemplate, design, templateArea, ct);
        // Measuring both garments needs no print area on the model photo; when either can't be measured, its print
        // area is the fallback.
        var layers = MockupRules.Layers(model, design, color);
        MockupPosition position;
        if (MockupRules.IsAutomaticPlacement(model.PrintAreaConfig) && await PlaceByGarmentAsync(used, garmentTemplate, model, ct) is [var first, .. var rest])
        {
            // On a photo of several people, each one wears the design.
            position = first;
            if (rest.Length > 0)
                layers = layers with { MorePlacements = rest };
        }
        else if (MockupRules.ParsePrintQuad(model.PrintAreaConfig) is { } quad && design is { ImageWidthPx: > 0, ImageHeightPx: > 0 })
        {
            // A print area marked by its corners: the design is warped onto the part of it it covers on the mock-up.
            var corners = MockupRules.MapPlacementOnQuad(used, templateArea, design.ImageWidthPx, design.ImageHeightPx, quad);
            position = MockupRules.QuadBounds(corners);
            layers = layers with { DesignCorners = corners };
        }
        else
            position = MockupRules.MapPlacement(used, templateArea, modelArea, model.OutputWidthPx, model.OutputHeightPx);

        string url;
        try
        {
            url = compositor.BuildCompositeUrl(model.BaseImageUrl, design.StorageKey, position, layers);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<Composed>(MockupErrors.TemplateNotUsable(model.Name));
        }

        return Result.Success(new Composed(url, userId, garment, design, model, color));
    }


    /// <summary>
    /// The print area the mock-up was made with. A job may have moved it for this mock-up alone; that one is told
    /// apart by making the mock-up again with it and getting the same image. Anything else used the template's own.
    /// </summary>
    private async Task<MockupPosition> UsedPrintAreaAsync(MockupImage garment, MockupTemplate template, DesignImage design,
        MockupPosition templateArea, CancellationToken ct)
    {
        if (garment.BatchJobProductId is not Guid rowId || await rows.GetByIdAsync(rowId, ct) is not { } row
            || await batches.GetByIdAsync(row.BatchJobId, ct) is not { } batch)
            return templateArea;

        var moved = MockupRules.ReadOverrides(batch.Config)
            .Where(entry => entry.Value is { Removed: false, PrintArea: not null } && entry.Value.MockupTemplateId == template.Id
                && MockupRules.TryReadSlotKey(entry.Key, out var slotDesignId, out _) && slotDesignId == design.Id)
            .Select(entry => new MockupPosition(entry.Value.PrintArea!.X, entry.Value.PrintArea.Y, entry.Value.PrintArea.Width, entry.Value.PrintArea.Height))
            .Distinct()
            .ToList();
        if (moved.Count == 0)
            return templateArea;

        var layers = MockupRules.Layers(template, design, garment.GarmentColor, garment.BackgroundColor);
        try
        {
            return moved.FirstOrDefault(area => compositor.BuildCompositeUrl(template.BaseImageUrl, design.StorageKey, area, layers) == garment.MockupImageUrl)
                ?? templateArea;
        }
        catch (InvalidOperationException)
        {
            return templateArea;
        }
    }

    /// <summary>The garment's own color on a template photo, or <see langword="null"/> when it can't be read.</summary>
    private async Task<string?> PhotoGarmentColorAsync(MockupTemplate template, CancellationToken ct)
    {
        if (!MockupRules.HasCurrentMaps(template))
            return null;
        try
        {
            return await maps.MeasureGarmentColorAsync(template.BaseImageUrl,
                compositor.BuildAssetUrl(template.BaseImageUrl, MockupRules.GarmentMaskKey(template)), ct);
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(error, "Could not read the garment color of template {TemplateId}", template.Id);
            return null;
        }
    }

    /// <summary>
    /// Where the design goes on the model photo by measuring the garment on both photos: one place, or one per
    /// person on a photo of several people. Empty when either can't be measured (no mask, or one that did not
    /// separate the garment).
    /// </summary>
    private async Task<MockupPosition[]> PlaceByGarmentAsync(MockupPosition used, MockupTemplate template, MockupTemplate model, CancellationToken ct)
    {
        if (!MockupRules.HasCurrentMaps(template) || !MockupRules.HasCurrentMaps(model))
            return [];
        try
        {
            var onTemplate = await maps.MeasureGarmentAsync(compositor.BuildAssetUrl(template.BaseImageUrl, MockupRules.GarmentMaskKey(template)), ct);
            if (onTemplate is null)
                return [];
            var modelMask = compositor.BuildAssetUrl(model.BaseImageUrl, MockupRules.GarmentMaskKey(model));
            IReadOnlyList<GarmentMeasurement> onModel = model.People == ModelPeople.Group
                ? await maps.MeasureGarmentsAsync(modelMask, ct)
                : await maps.MeasureGarmentAsync(modelMask, ct) is { } one ? [one] : [];
            return onModel
                .Select(garment => MockupRules.MapPlacementByGarment(used, onTemplate, template.OutputWidthPx, template.OutputHeightPx, garment, model.OutputWidthPx, model.OutputHeightPx))
                .ToArray();
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(error, "Could not measure the garments of template {TemplateId} and model photo {ModelPhotoId}", template.Id, model.Id);
            return [];
        }
    }

    private async Task<Result<MockupResponse>> ListedAsync(Guid productId, Guid id, CancellationToken ct)
    {
        var listed = await mockups.ListAsync(productId, ct);
        return listed.IsFailure ? Result.Failure<MockupResponse>(listed.Error) : Result.Success(listed.Value.Single(x => x.Id == id));
    }

    private static Error Missing() => Error.NotFound("MockupNotFound", "Product or mockup not found.");
}
