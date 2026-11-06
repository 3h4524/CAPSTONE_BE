using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.Entitlements;
using APCS.Application.Features.UsageStatistics;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Workflows;

public interface ILifestyleMockupService
{
    Task<Result<MockupResponse>> GenerateAsync(Guid productId, Guid garmentMockupId, Stream person, long length, CancellationToken ct);

    /// <summary>
    /// As <see cref="GenerateAsync"/>, with a model photo from the library (the shared one or the seller's own)
    /// instead of an uploaded photo. Everyone on a photo of several people is dressed in the mock-up.
    /// </summary>
    Task<Result<MockupResponse>> GenerateFromModelPhotoAsync(Guid productId, Guid garmentMockupId, Guid modelPhotoId, CancellationToken ct);
}

/// <summary>
/// Dresses the person (or people) in a photo in the product of an existing mock-up, using the Seller's Ideogram key.
/// </summary>
/// <remarks>
/// Ideogram edits the person photo with two reference images: the mock-up, for the cut and colour of the garment, and
/// the design the mock-up was made from, so the print is copied from the clean artwork rather than from the mock-up.
/// The result is stored like an uploaded mock-up with the Lifestyle role, in the garment's artwork
/// group, so a lifestyle AI Shot can use it. It stays pending: the seller still reviews the generated
/// image at the Mockup Approval gate. The person photo itself is not stored.
/// </remarks>
public sealed class LifestyleMockupService(ICurrentUser user, IRepository<Product> products, IRepository<MockupImage> assets,
    IRepository<DesignImage> designs, IRepository<ApiUsageRecord> usage, IApiKeyRepository keys, IApiKeyCredentials credentials,
    IEnumerable<IImageGenerationProvider> imageProviders, IMediaStorage storage, IMockupAssetService mockups, IUnitOfWork uow, TimeProvider time,
    ILogger<LifestyleMockupService> logger, IRepository<MockupTemplate> templates, IMockupMapService maps,
    IPlanEntitlementService entitlements) : ILifestyleMockupService
{
    // Ideogram edits the photo, the same provider the designs are made with.
    private readonly IImageGenerationProvider images = imageProviders.First(provider => provider.Provider == ImageGenerationProviders.Ideogram);

    public const int MaximumPersonBytes = 8 * 1024 * 1024;

    // A flat mock-up shows the inside of the back collar, often with a label: said here so it is not worn at the front.
    private const string GarmentPrompt = """
        Change only the top the person is wearing. Replace it with the garment from the reference photo of the product laid flat: the same garment type, cut, fit, sleeve shape, length and colour, now worn naturally on their body. The neckline sits naturally around their neck; do not copy any neck label, tag or the inside of the collar from that photo.
        """;

    private const string GroupGarmentPrompt = """
        Change only the tops the people are wearing. Dress every person in the garment from the reference photo of the product laid flat, the same on each of them: the same garment type, cut, fit, sleeve shape, length and colour, now worn naturally on their body and sized to it. Each neckline sits naturally around the neck; do not copy any neck label, tag or the inside of the collar from that photo.
        """;

    private const string KeepPrompt = """
        The fabric is photorealistic, with natural folds and soft shadows that follow the body, and the print follows the folds. Keep the person's face, identity, hair, skin, body, pose, other clothing, background, lighting and framing exactly as they are. No other text, logos, labels, watermarks or people.
        """;

    private const string GroupKeepPrompt = """
        The fabric is photorealistic, with natural folds and soft shadows that follow each body, and the print follows the folds. Keep every person's face, identity, hair, skin, body, pose and other clothing, and the background, lighting and framing, exactly as they are. No other text, logos, labels, watermarks or people.
        """;

    /// <summary>
    /// The edit instruction: the print comes from the design when the mock-up has one, otherwise from the mock-up; on
    /// a photo of several people, everyone wears it.
    /// </summary>
    public static string Prompt(bool withDesign, string? designText, bool group = false)
    {
        var print = withDesign
            ? $"Print the artwork from the other reference image (the artwork alone, on a transparent background) on {(group ? "each garment" : "the garment")}, reproduced exactly: the same illustrations, lettering, colours and proportions, at the same place and size as on the product photo."
            : "Keep the artwork printed on the product photo, reproduced exactly: the same illustrations, lettering, colours and proportions, at the same place and size.";
        var text = string.IsNullOrWhiteSpace(designText) ? "" : $" The printed lettering reads exactly \"{designText.Trim().Replace('"', '\'')}\".";
        return group
            ? $"{GroupGarmentPrompt.Trim()} {print}{text} {GroupKeepPrompt.Trim()}"
            : $"{GarmentPrompt.Trim()} {print}{text} {KeepPrompt.Trim()}";
    }

    public async Task<Result<MockupResponse>> GenerateAsync(Guid productId, Guid garmentMockupId, Stream person, long length, CancellationToken ct)
    {
        var found = await FindAsync(productId, garmentMockupId, ct);
        if (found.IsFailure) return Result.Failure<MockupResponse>(found.Error);
        if (length is <= 0 or > MaximumPersonBytes) return Result.Failure<MockupResponse>(TooLarge());

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await person.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaximumPersonBytes) return Result.Failure<MockupResponse>(TooLarge());
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var personBytes = buffer.ToArray();
        if (!MockupAssetService.IsStaticImage(personBytes)) return Result.Failure<MockupResponse>(Error.Validation("Use a static JPEG, PNG or WebP image."));

        var (userId, product, garment) = found.Value;
        return await DressAsync(userId, product, garment, personBytes, group: false, ct);
    }

    public async Task<Result<MockupResponse>> GenerateFromModelPhotoAsync(Guid productId, Guid garmentMockupId, Guid modelPhotoId, CancellationToken ct)
    {
        var found = await FindAsync(productId, garmentMockupId, ct);
        if (found.IsFailure) return Result.Failure<MockupResponse>(found.Error);
        var (userId, product, garment) = found.Value;

        var model = await templates.Query().SingleOrDefaultAsync(item => item.Id == modelPhotoId && item.IsActive == true
            && item.Kind == MockupRules.ModelKind && (item.UserId == null || item.UserId == userId), ct);
        if (model is null) return Result.Failure<MockupResponse>(MockupErrors.ModelPhotoNotFound());

        byte[] personBytes;
        try { personBytes = await maps.DownloadAsync(model.BaseImageUrl, ct); }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(error, "Could not read model photo {ModelPhotoId} for a lifestyle image", model.Id);
            return Result.Failure<MockupResponse>(Error.Failure("LifestyleGenerationFailed", "Could not read the model photo. Try again."));
        }

        return await DressAsync(userId, product, garment, personBytes, group: model.People == ModelPeople.Group, ct);
    }

    // The Seller's own product, and a mock-up of it that can be worn, when the Seller's plan includes lifestyle images.
    private async Task<Result<(Guid UserId, Product Product, MockupImage Garment)>> FindAsync(Guid productId, Guid garmentMockupId, CancellationToken ct)
    {
        if (user.UserId is not Guid userId || await products.GetByIdAsync(productId, ct) is not { DeletedAt: null } product || product.UserId != userId)
            return Result.Failure<(Guid, Product, MockupImage)>(Missing());
        var planCheck = await entitlements.RequireFeatureAsync(userId, PlanFeatures.LifestyleMockup, ct);
        if (planCheck.IsFailure) return Result.Failure<(Guid, Product, MockupImage)>(planCheck.Error);
        var garment = await assets.GetByIdAsync(garmentMockupId, ct);
        // A batch mock-up is only its composite link until the video imports it; either one can be worn.
        if (garment is null || garment.DeletedAt != null || garment.ProductId != productId
            || (garment.ContentHash == null && (garment.SourceType != MockupSources.Generated || garment.MockupImageUrl == null)))
            return Result.Failure<(Guid, Product, MockupImage)>(Missing());
        return Result.Success((userId, product, garment));
    }

    private async Task<Result<MockupResponse>> DressAsync(Guid userId, Product product, MockupImage garment, byte[] personBytes, bool group, CancellationToken ct)
    {
        var productId = product.Id;
        var key = (await keys.ListOwnedAsync(userId, ct)).FirstOrDefault(x =>
            x.ServiceProvider == ImageGenerationProviders.Ideogram && x.IsActive == true && x.LastCheckSucceeded == true);
        if (key is null) return Result.Failure<MockupResponse>(Error.Conflict("MissingApiKey", "Connect a valid Ideogram API key before generating a lifestyle image."));

        // An uploaded mock-up has no design: its own print is all there is to copy.
        var design = garment.DesignImageId is Guid designId && await designs.GetByIdAsync(designId, ct) is { DeletedAt: null } found ? found : null;

        ImageGenerationResult generated;
        try
        {
            var garmentBytes = garment.ContentHash != null
                ? await storage.ReadImageAsync(garment.StorageKey, garment.StorageVersion, ct)
                : await storage.ReadCompositeAsync(garment.MockupImageUrl!, ct);
            List<ImageReference> sent = [new(personBytes, MimeType(personBytes)), new(garmentBytes, MimeType(garmentBytes))];
            if (design is not null) sent.Add(new(await storage.ReadPublicImageAsync(design.StorageKey, ct), "image/png"));
            generated = await images.ComposeAsync(credentials.Unprotect(key.KeyValueEncrypted),
                Prompt(design is not null, design is null ? null : product.DesiredDesignText, group), sent, ct);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(error, "Could not read mock-up {MockupId} for a lifestyle image", garment.Id);
            return Result.Failure<MockupResponse>(Error.Failure("LifestyleGenerationFailed", "Could not read the product mock-up. Try again."));
        }
        if (!generated.IsSuccess)
        {
            logger.LogWarning("Lifestyle image for product {ProductId} was not generated: {Reason}", productId, generated.ErrorMessage);
            return Result.Failure<MockupResponse>(generated.IsBlocked
                ? Error.Validation("Ideogram declined to edit this photo. Use a different photo.")
                : Error.Failure("LifestyleGenerationFailed", "Ideogram could not generate the lifestyle image. Try again."));
        }

        var id = Guid.NewGuid();
        var now = time.GetUtcNow().UtcDateTime;
        using var content = new MemoryStream(generated.ImageBytes!);
        StoredMockup saved;
        try { saved = await storage.UploadMockupAsync(content, $"{id}.png", $"mockups/{userId}/{productId}/{id}", ct); }
        catch (InvalidOperationException) { return Result.Failure<MockupResponse>(Error.Failure("LifestyleGenerationFailed", "Could not store the generated image. Try again.")); }

        await usage.AddAsync(new ApiUsageRecord
        {
            Id = Guid.NewGuid(), UserId = userId, ProductId = productId, Provider = ImageGenerationProviders.Ideogram, Feature = UsageFeatures.Mockup,
            ModelName = generated.ModelUsed, RequestUnits = 1, CostUsd = generated.EstimatedCostUsd, Status = "success", CreatedAt = now
        }, cancellationToken: ct);
        await assets.AddAsync(new MockupImage
        {
            Id = id, ProductId = productId, SourceType = MockupSources.Uploaded, StorageProvider = "cloudinary", StorageKey = saved.StorageKey,
            StorageVersion = saved.Version, ContentHash = saved.Hash, MockupWidthPx = saved.Width, MockupHeightPx = saved.Height,
            Role = "Lifestyle", ArtworkGroupKey = garment.ArtworkGroupKey ?? productId.ToString(), VariantKey = garment.VariantKey,
            MetadataRevision = MockupSources.FirstRevision, ApprovalStatus = "pending", Regions = WorkflowJson.Write(new MockupRegions()),
            IsFinal = false, CreatedAt = now
        }, cancellationToken: ct);
        await uow.SaveChangesAsync(ct);

        var listed = await mockups.ListAsync(productId, ct);
        return listed.IsFailure ? Result.Failure<MockupResponse>(listed.Error) : Result.Success(listed.Value.Single(x => x.Id == id));
    }

    private static string MimeType(byte[] bytes) => bytes[0] == 0xff ? "image/jpeg" : bytes[0] == 137 ? "image/png" : "image/webp";
    private static Error Missing() => Error.NotFound("MockupNotFound", "Product or mockup not found.");
    private static Error TooLarge() => Error.Validation("The person photo must be at most 8 MB.");
}
