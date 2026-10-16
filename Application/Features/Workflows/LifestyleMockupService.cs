using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Workflows;

public interface ILifestyleMockupService
{
    Task<Result<MockupResponse>> GenerateAsync(Guid productId, Guid garmentMockupId, Stream person, long length, CancellationToken ct);
}

/// <summary>
/// Dresses the adult in an uploaded photo in the product of an existing mock-up, using the Seller's Gemini key.
/// </summary>
/// <remarks>
/// The result is stored like an uploaded mock-up with the Lifestyle role, in the garment's artwork
/// group, so a lifestyle AI Shot can use it. It stays pending: the seller still reviews the generated
/// image at the Mockup Approval gate. The person photo itself is not stored.
/// </remarks>
public sealed class LifestyleMockupService(ICurrentUser user, IRepository<Product> products, IRepository<MockupImage> assets,
    IRepository<ApiUsageRecord> usage, IApiKeyRepository keys, IApiKeyCredentials credentials, IImageGenerationProvider images,
    IMediaStorage storage, IMockupAssetService mockups, IUnitOfWork uow, TimeProvider time,
    ILogger<LifestyleMockupService> logger) : ILifestyleMockupService
{
    // Both images travel inline in one Gemini request, which is capped at about 20 MB.
    public const int MaximumPersonBytes = 8 * 1024 * 1024;

    private const string Prompt = """
        Edit the first image, a photo of an adult person. The second image is a product mock-up.
        Dress the person in the product from the second image: the same garment type and colour, with the printed artwork reproduced exactly - same design, text, colours, position and scale on the garment.
        Keep the person's face, identity, hair, skin, body, pose, other clothing, background, lighting and framing unchanged.
        Make the fabric photorealistic, with natural folds and shadows that follow the body.
        Do not add text, logos, watermarks, borders or other people. Return one image with the same aspect ratio as the first image.
        """;

    public async Task<Result<MockupResponse>> GenerateAsync(Guid productId, Guid garmentMockupId, Stream person, long length, CancellationToken ct)
    {
        if (user.UserId is not Guid userId || await products.GetByIdAsync(productId, ct) is not { DeletedAt: null } product || product.UserId != userId)
            return Result.Failure<MockupResponse>(Missing());
        var garment = await assets.GetByIdAsync(garmentMockupId, ct);
        if (garment is null || garment.DeletedAt != null || garment.ProductId != productId || garment.ContentHash == null)
            return Result.Failure<MockupResponse>(Missing());
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

        var key = (await keys.ListOwnedAsync(userId, ct)).FirstOrDefault(x => x.ServiceProvider == "gemini" && x.IsActive == true && x.LastCheckSucceeded == true);
        if (key is null) return Result.Failure<MockupResponse>(Error.Conflict("MissingApiKey", "Connect a valid Gemini API key before generating a lifestyle image."));

        ImageGenerationResult generated;
        try
        {
            var garmentBytes = await storage.ReadImageAsync(garment.StorageKey, garment.StorageVersion, ct);
            generated = await images.ComposeAsync(credentials.Unprotect(key.KeyValueEncrypted), Prompt,
                [new(personBytes, MimeType(personBytes)), new(garmentBytes, "image/png")], ct);
        }
        catch (HttpRequestException error)
        {
            logger.LogWarning(error, "Could not read mock-up {MockupId} for a lifestyle image", garment.Id);
            return Result.Failure<MockupResponse>(Error.Failure("LifestyleGenerationFailed", "Could not read the product mock-up. Try again."));
        }
        if (!generated.IsSuccess)
        {
            logger.LogWarning("Lifestyle image for product {ProductId} was not generated: {Reason}", productId, generated.ErrorMessage);
            return Result.Failure<MockupResponse>(generated.IsBlocked
                ? Error.Validation("Gemini declined to edit this photo. Use a different photo of an adult.")
                : Error.Failure("LifestyleGenerationFailed", "Gemini could not generate the lifestyle image. Try again."));
        }

        var id = Guid.NewGuid();
        var now = time.GetUtcNow().UtcDateTime;
        using var content = new MemoryStream(generated.ImageBytes!);
        StoredMockup saved;
        try { saved = await storage.UploadMockupAsync(content, $"{id}.png", $"mockups/{userId}/{productId}/{id}", ct); }
        catch (InvalidOperationException) { return Result.Failure<MockupResponse>(Error.Failure("LifestyleGenerationFailed", "Could not store the generated image. Try again.")); }

        await usage.AddAsync(new ApiUsageRecord
        {
            Id = Guid.NewGuid(), UserId = userId, ProductId = productId, Provider = "gemini", Feature = "image_generation",
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
