using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Workflows;

public interface IAiStudioFrameService
{
    Task<Result<AiStudioFrameQuote>> QuoteAsync(Guid productId, AiStudioFrameRequest request, CancellationToken ct);
    Task<Result<AiStudioFrameGenerationResponse>> GenerateAsync(Guid productId, AiStudioFrameRequest request, CancellationToken ct);
}

/// <summary>Prepares, prices and reuses reviewed product studio keyframes for showcase scenes.</summary>
public sealed class AiStudioFrameService(ICurrentUser user, IRepository<Product> products, IRepository<MockupImage> assets,
    IRepository<ApiUsageRecord> usage, IApiKeyRepository keys, IApiKeyCredentials credentials, IImageGenerationProvider images,
    IMediaStorage storage, IMockupAssetService mockups, IUnitOfWork uow, TimeProvider time,
    ILogger<AiStudioFrameService> logger) : IAiStudioFrameService
{
    public const int MaximumSourceBytes = 20 * 1024 * 1024;
    public const int MaximumReferenceCount = 3;
    public const int MaximumCombinedSourceBytes = 40 * 1024 * 1024;

    private sealed record ResolvedReference(MockupImage Asset);
    private sealed record StudioFrameContext(Product Product, MockupImage Source,
        IReadOnlyList<ResolvedReference> References, string AspectRatio, string Fingerprint);

    public async Task<Result<AiStudioFrameQuote>> QuoteAsync(Guid productId, AiStudioFrameRequest request, CancellationToken ct)
    {
        var context = await ResolveAsync(productId, request, ct);
        if (context.IsFailure) return Result.Failure<AiStudioFrameQuote>(context.Error);
        var (_, source, references, aspectRatio, fingerprint) = context.Value;

        MockupResponse? existing = null;
        if (!request.Regenerate)
        {
            var candidates = await assets.FindAsync(x => x.ProductId == productId && x.DeletedAt == null &&
                x.SourceType == MockupSources.AiGenerated && x.ApprovalStatus != "rejected", ct);
            var match = candidates.Where(frame =>
                    AiStudioFrameIdentity.Read(frame.Regions)?.Fingerprint == fingerprint &&
                    AiStudioFrameIdentity.IsCurrent(frame, source, request.Layout, aspectRatio, request.CustomPrompt,
                        request.TemplateCode, references.Select(reference => reference.Asset.Id).ToArray()))
                .OrderBy(frame => frame.ApprovalStatus == "approved" && frame.ApprovedRevision == frame.MetadataRevision ? 0 : 1)
                .ThenByDescending(frame => frame.CreatedAt).FirstOrDefault();
            if (match is not null)
            {
                var listed = await mockups.ListAsync(productId, ct);
                if (listed.IsFailure) return Result.Failure<AiStudioFrameQuote>(listed.Error);
                existing = listed.Value.SingleOrDefault(frame => frame.Id == match.Id);
            }
        }

        var isApproved = existing is { ApprovalStatus: "approved" } && existing.ApprovedRevision == existing.Revision;
        return Result.Success(new AiStudioFrameQuote(fingerprint,
            AiStudioFrameIdentity.PromptVersionFor(request.Layout, request.CustomPrompt),
            existing is null ? images.EstimatedCostPerImageUsd : 0m, existing,
            existing is null
                ? "This Gemini image estimate is for one new studio keyframe. A new frame must be approved before it can be sent to Veo."
                : isApproved
                    ? "A matching approved studio keyframe already exists and can be reused without another image generation call."
                    : "A matching unreviewed keyframe already exists. Review and approve it before using it in a video; no new image call is needed."));
    }

    public async Task<Result<AiStudioFrameGenerationResponse>> GenerateAsync(Guid productId, AiStudioFrameRequest request, CancellationToken ct)
    {
        var quote = await QuoteAsync(productId, request, ct);
        if (quote.IsFailure) return Result.Failure<AiStudioFrameGenerationResponse>(quote.Error);
        if (quote.Value.ExistingFrame is { } reusable && !request.Regenerate)
            return Result.Success(new AiStudioFrameGenerationResponse(reusable, true, 0m));
        if (string.IsNullOrWhiteSpace(request.ExpectedFingerprint) || request.ExpectedFingerprint != quote.Value.Fingerprint ||
            request.ConfirmedEstimatedCostUsd is null || request.ConfirmedEstimatedCostUsd.Value != quote.Value.EstimatedCostUsd)
            return Result.Failure<AiStudioFrameGenerationResponse>(Error.Conflict("StudioFrameQuoteChanged", "The source or image estimate changed. Review the updated keyframe quote before generating."));

        var context = await ResolveAsync(productId, request, ct);
        if (context.IsFailure) return Result.Failure<AiStudioFrameGenerationResponse>(context.Error);
        var (_, source, references, aspectRatio, fingerprint) = context.Value;
        var userId = user.UserId!.Value;
        var key = (await keys.ListOwnedAsync(userId, ct)).FirstOrDefault(x => x.ServiceProvider == "gemini" && x.IsActive == true && x.LastCheckSucceeded == true);
        if (key is null)
            return Result.Failure<AiStudioFrameGenerationResponse>(Error.Conflict("MissingApiKey", "Connect a valid Gemini API key before generating a studio keyframe."));

        ImageGenerationResult generated;
        try
        {
            var providerReferences = new List<ImageReference>(references.Count + 1);
            var sourceBytes = await storage.ReadImageAsync(source.StorageKey, source.StorageVersion, ct);
            if (sourceBytes.Length is <= 0 or > MaximumSourceBytes || DetectMimeType(sourceBytes) is not { } sourceMimeType)
                return Result.Failure<AiStudioFrameGenerationResponse>(Error.Validation("The approved source must be a PNG, JPEG or WebP image no larger than 20 MB."));
            providerReferences.Add(new ImageReference(sourceBytes, sourceMimeType));
            var combinedBytes = sourceBytes.Length;
            foreach (var reference in references)
            {
                var bytes = await storage.ReadImageAsync(reference.Asset.StorageKey, reference.Asset.StorageVersion, ct);
                if (bytes.Length is <= 0 or > MaximumSourceBytes || DetectMimeType(bytes) is not { } mimeType)
                    return Result.Failure<AiStudioFrameGenerationResponse>(Error.Validation("Every reference must be a PNG, JPEG or WebP image no larger than 20 MB."));
                combinedBytes += bytes.Length;
                if (combinedBytes > MaximumCombinedSourceBytes)
                    return Result.Failure<AiStudioFrameGenerationResponse>(Error.Validation("The product source and reference images must total 40 MB or less."));
                providerReferences.Add(new ImageReference(bytes, mimeType));
            }
            generated = await images.ComposeAsync(credentials.Unprotect(key.KeyValueEncrypted),
                BuildPrompt(context.Value.Product.ProductType, source, request.Layout, aspectRatio, request.CustomPrompt,
                    request.TemplateCode, references), providerReferences, ct);
        }
        catch (HttpRequestException error)
        {
            logger.LogWarning(error, "Could not read approved mock-up {MockupId} for an AI studio keyframe", source.Id);
            return Result.Failure<AiStudioFrameGenerationResponse>(Error.Failure("StudioFrameGenerationFailed", "Could not read the approved product image. Try again."));
        }

        if (!generated.IsSuccess)
        {
            logger.LogWarning("Studio keyframe for product {ProductId} was not generated: {Reason}", productId, generated.ErrorMessage);
            return Result.Failure<AiStudioFrameGenerationResponse>(generated.IsBlocked
                ? Error.Validation("Gemini declined to generate this studio keyframe. Try a different product image or layout.")
                : Error.Failure("StudioFrameGenerationFailed", "Gemini could not generate the studio keyframe. Try again."));
        }
        if (!MockupAssetService.IsStaticImage(generated.ImageBytes!) || !AllowedMimeType(generated.MimeType))
            return Result.Failure<AiStudioFrameGenerationResponse>(Error.Failure("StudioFrameGenerationFailed", "Gemini returned an unsupported image. Try again."));

        var id = Guid.NewGuid();
        var now = time.GetUtcNow().UtcDateTime;
        var referenceMetadata = references.Select(reference => new AiStudioFrameReferenceMetadata(reference.Asset.Id,
            reference.Asset.ContentHash!, reference.Asset.MetadataRevision)).ToArray();
        var metadata = new AiStudioFrameMetadata(fingerprint, source.Id, source.ContentHash!, source.MetadataRevision,
            request.Layout, aspectRatio, AiStudioFrameIdentity.PromptVersionFor(request.Layout, request.CustomPrompt),
            AiStudioFrameIdentity.NormalizeCustomPrompt(request.CustomPrompt),
            AiStudioFrameIdentity.NormalizeTemplateCode(request.TemplateCode), referenceMetadata);
        using var content = new MemoryStream(generated.ImageBytes!);
        StoredMockup saved;
        try
        {
            saved = await storage.UploadMockupAsync(content, $"{id}{FileExtension(generated.MimeType!)}",
                $"mockups/{userId}/{productId}/{id}", ct);
        }
        catch (InvalidOperationException error)
        {
            logger.LogWarning(error, "Could not store AI studio keyframe for product {ProductId}", productId);
            return Result.Failure<AiStudioFrameGenerationResponse>(Error.Failure("StudioFrameGenerationFailed", "Could not store the generated studio keyframe. Try again."));
        }

        var usageId = Guid.NewGuid();
        await usage.AddAsync(new ApiUsageRecord
        {
            Id = usageId, UserId = userId, ProductId = productId, Provider = "gemini", Feature = "image_generation",
            ModelName = generated.ModelUsed, RequestUnits = 1, CostUsd = generated.EstimatedCostUsd, Status = "success", CreatedAt = now
        }, cancellationToken: ct);
        await assets.AddAsync(new MockupImage
        {
            Id = id, ProductId = productId, ApiUsageRecordId = usageId, SourceType = MockupSources.AiGenerated,
            StorageProvider = "cloudinary", StorageKey = saved.StorageKey, StorageVersion = saved.Version, ContentHash = saved.Hash,
            MockupWidthPx = saved.Width, MockupHeightPx = saved.Height, Role = "VideoScene",
            ArtworkGroupKey = source.ArtworkGroupKey ?? productId.ToString(), MetadataRevision = MockupSources.FirstRevision,
            ApprovalStatus = "pending", Regions = AiStudioFrameIdentity.SerializeRegions(metadata), IsFinal = false, CreatedAt = now
        }, cancellationToken: ct);
        await uow.SaveChangesAsync(ct);

        var listedAssets = await mockups.ListAsync(productId, ct);
        if (listedAssets.IsFailure) return Result.Failure<AiStudioFrameGenerationResponse>(listedAssets.Error);
        var response = listedAssets.Value.Single(frame => frame.Id == id);
        return Result.Success(new AiStudioFrameGenerationResponse(response, false, generated.EstimatedCostUsd));
    }

    private async Task<Result<StudioFrameContext>> ResolveAsync(
        Guid productId, AiStudioFrameRequest request, CancellationToken ct)
    {
        if (user.UserId is not Guid userId || await products.GetByIdAsync(productId, ct) is not { DeletedAt: null } product || product.UserId != userId)
            return Result.Failure<StudioFrameContext>(Error.NotFound("ProductNotFound", "Product not found."));
        if (!AiStudioFrameIdentity.IsSupportedLayout(request.Layout))
            return Result.Failure<StudioFrameContext>(Error.Validation("Choose a supported keyframe template."));
        if (request.CustomPrompt?.Length > 500)
            return Result.Failure<StudioFrameContext>(Error.Validation("Keep custom keyframe directions under 500 characters."));
        if (!VideoOutputFormats.IsSupported(request.OutputFormat))
            return Result.Failure<StudioFrameContext>(Error.Validation("Choose a supported video output format."));
        var source = await assets.GetByIdAsync(request.SourceMockupId, ct);
        if (source is null || source.ProductId != productId || source.DeletedAt != null || source.ContentHash is null ||
            source.ApprovalStatus != "approved" || source.ApprovedRevision != source.MetadataRevision)
            return Result.Failure<StudioFrameContext>(Error.Conflict("SourceNotApproved", "Choose a current, approved product image as the keyframe source."));

        var requestedReferences = AiStudioFrameIdentity.NormalizeReferenceIds(request.ReferenceMockupIds);
        if (requestedReferences.Count > MaximumReferenceCount)
            return Result.Failure<StudioFrameContext>(Error.Validation($"Use no more than {MaximumReferenceCount} keyframe reference images."));
        if (requestedReferences.Distinct().Count() != requestedReferences.Count || requestedReferences.Contains(source.Id))
            return Result.Failure<StudioFrameContext>(Error.Validation("Choose different images for the product source and each keyframe reference."));
        if (RequiresPersonRights(request.TemplateCode) && !request.ReferenceRightsConfirmed)
            return Result.Failure<StudioFrameContext>(Error.Validation("Confirm that you have rights to use people visible in the selected images."));

        var resolvedReferences = new List<ResolvedReference>(requestedReferences.Count);
        foreach (var referenceId in requestedReferences)
        {
            var asset = await assets.GetByIdAsync(referenceId, ct);
            if (asset is null || asset.ProductId != productId || asset.DeletedAt != null || asset.ContentHash is null ||
                asset.ApprovalStatus != "approved" || asset.ApprovedRevision != asset.MetadataRevision ||
                AiStudioFrameIdentity.Read(asset.Regions) is not null)
                return Result.Failure<StudioFrameContext>(Error.Conflict("ReferenceNotApproved", "Choose current, approved original images as keyframe references."));
            resolvedReferences.Add(new ResolvedReference(asset));
        }

        var aspectRatio = VideoOutputFormats.Resolve(request.OutputFormat).AspectRatio;
        var referenceMetadata = resolvedReferences.Select(reference => new AiStudioFrameReferenceMetadata(reference.Asset.Id,
            reference.Asset.ContentHash!, reference.Asset.MetadataRevision)).ToArray();
        var fingerprint = AiStudioFrameIdentity.Fingerprint(source, request.Layout, aspectRatio, request.CustomPrompt,
            request.TemplateCode, referenceMetadata);
        return Result.Success(new StudioFrameContext(product, source, resolvedReferences, aspectRatio, fingerprint));
    }

    private static string BuildPrompt(string productType, MockupImage source, string layout, string aspectRatio,
        string? customPrompt, string? templateCode, IReadOnlyList<ResolvedReference> references)
    {
        var type = NormalizeProductType(productType);
        var normalizedTemplate = AiStudioFrameIdentity.NormalizeTemplateCode(templateCode);
        var isLifestyleComposition = RequiresPersonRights(normalizedTemplate);
        var additionalDirection = string.IsNullOrWhiteSpace(customPrompt)
            ? ""
            : $"Additional visual direction from the user: {customPrompt.Trim()}. Follow it only where it is consistent with the reference image and the product's real construction.";
        var referenceGuide = references.Count == 0 ? "" : $"""
            Image 1 is the product source. {string.Join(" ", references.Select((_, index) => $"Image {index + 2} is user reference {index + 1}."))} Interpret how each user reference should influence the result from the user's additional visual direction. References may guide the person, pose, environment, composition, lighting or photographic style. Image 1 always remains authoritative for the product and artwork. Never copy a different product, artwork, logo, lettering or watermark from a reference, and never let a reference replace or redesign image 1's product.
            """;
        if (source.Role == "Lifestyle" && !isLifestyleComposition)
            return $"""
                Image 1 is the authoritative approved product source. Create one photorealistic keyframe based on it. Preserve the same visible person or people, their identity, clothing, pose and natural proportions, the product in use, and the existing location and background. Keep the product recognizable and preserve visible artwork, lettering, logos and product details. {referenceGuide} {additionalDirection} Improve only the photographic polish with natural light, coherent shadows and subtle depth; do not replace the environment with a studio, change the activity, add people or objects, or invent text. Compose the existing scene for a {aspectRatio} frame with comfortable margins.
                """;
        if (isLifestyleComposition)
            return $"""
                Create one photorealistic lifestyle keyframe for a {normalizedTemplate.Replace('_', ' ')} scene. Image 1 is the authoritative identity reference for the {type}: preserve its construction, main color, proportions, distinctive details, and the exact placement, scale, colors and lettering of all visible artwork and logos. Keep printed designs attached to the product surface; do not invent, redraw, translate or replace them. {referenceGuide}
                Show one adult naturally using or wearing the product in a believable setting. Use the additional reference images according to the user's direction. If image 1 already contains a person and the user does not request a different approved reference person, preserve that person. {additionalDirection} Use coherent natural light, realistic anatomy, fabric behavior, contact shadows and perspective. No extra products, copied reference branding, added text, borders or watermarks. Compose for a {aspectRatio} frame with comfortable margins.
                """;
        var staging = (type, layout) switch
        {
            (_, AiStudioFrameIdentity.None) => "Do not apply a preset arrangement. Follow the user's custom visual direction for staging; if none is given, preserve the product's arrangement from the reference. Keep it naturally supported and grounded, with believable contact shadows.",
            ("T-shirt" or "hoodie" or "tote bag", AiStudioFrameIdentity.HangingWindow) => $"Stage the same {type} hanging naturally from a suitable simple hanger or hook against a warm neutral studio wall. Show soft window daylight, realistic contact shadows, and natural gravity-driven fabric folds; its support is visible and the product does not float.",
            ("T-shirt" or "hoodie" or "tote bag", _) => $"Stage the same {type} resting naturally on a matte warm-neutral studio surface. Show believable gravity-driven folds, soft contact shadows, gentle window daylight and a quiet premium product-photography atmosphere.",
            ("poster", AiStudioFrameIdentity.HangingWindow) => "Stage the same poster upright on a simple neutral studio wall, with its edges supported naturally. Use soft window daylight and a realistic wall contact shadow; preserve its flat paper surface.",
            ("poster", _) => "Stage the same poster flat on a matte warm-neutral studio surface. Keep the paper flat with only subtle natural edge lift and soft contact shadows under gentle window daylight.",
            (_, AiStudioFrameIdentity.HangingWindow) => $"Stage the same {type} upright in a simple, stable product display on a warm neutral studio surface, at a natural viewing angle. Use soft window daylight and a grounded contact shadow; do not suspend or float it.",
            _ => $"Stage the same {type} resting securely on a matte warm-neutral studio surface at a natural viewing angle. Use soft window daylight and a grounded contact shadow; keep its shape rigid and do not make it float."
        };
        var background = layout == AiStudioFrameIdentity.None
            ? "Use the environment requested in the custom direction, or preserve the reference background when no environment is specified."
            : "Use a simple natural background with realistic light and shadow.";
        var framing = layout == AiStudioFrameIdentity.None && !string.IsNullOrWhiteSpace(customPrompt)
            ? "Follow the requested framing. If a close-up is requested, fill the frame with the specified artwork or material detail instead of showing the whole item."
            : "Keep one product only, centered with the whole item visible.";
        return $"""
            Create one photorealistic studio product photograph. Image 1 is the authoritative identity reference for the {type} and every visible product detail or artwork. {referenceGuide} {staging}
            {framing} Preserve the visible product construction, main color, proportions, distinctive details and the placement, scale, colors and lettering of any artwork or logo. Keep printed designs attached to the product surface and do not invent or redraw them. {additionalDirection} {background} No extra products, added text, extra logos, borders or watermarks. Compose for a {aspectRatio} frame with comfortable margins.
            """;
    }

    private static bool RequiresPersonRights(string? templateCode) =>
        AiStudioFrameIdentity.NormalizeTemplateCode(templateCode) is "lifestyle_in_use" or "creator_pov_ugc" or "everyday_story_moment";

    private static string NormalizeProductType(string productType) => productType.Trim().ToLowerInvariant() switch
    {
        "tshirt" or "t-shirt" or "t_shirt" => "T-shirt",
        "hoodie" => "hoodie",
        "mug" => "mug",
        "poster" => "poster",
        "tote_bag" => "tote bag",
        "phone_case" => "phone case",
        _ => "product"
    };

    private static bool AllowedMimeType(string? mimeType) => mimeType is "image/png" or "image/jpeg" or "image/webp";
    private static string FileExtension(string mimeType) => mimeType switch { "image/jpeg" => ".jpg", "image/webp" => ".webp", _ => ".png" };
    private static string? DetectMimeType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47)
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            return "image/jpeg";
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "image/webp";
        return null;
    }
}
