using System.Security.Cryptography;
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

    /// <summary>
    /// As <see cref="GenerateFromModelPhotoAsync"/>, made by Ideogram with the Seller's Ideogram key (or the platform's): the image a batch
    /// video run makes by itself for a design whose AI scenes show a person (<see cref="PersonModelPhotos"/>).
    /// </summary>
    Task<Result<MockupResponse>> GenerateForVideoAsync(Guid productId, Guid garmentMockupId, Guid modelPhotoId, CancellationToken ct);
}

/// <summary>
/// Dresses the person (or people) in a photo in the product of an existing mock-up, using the Seller's Ideogram key (or the
/// platform's, on a plan with <see cref="PlanFeatures.SystemImageKeys"/>), for the image they ask for and for the one a
/// video run makes by itself (<see cref="GenerateForVideoAsync"/>).
/// </summary>
/// <remarks>
/// The model edits the person photo with two reference images: the mock-up, for the cut and colour of the garment, and
/// the design the mock-up was made from, so the print is copied from the clean artwork rather than from the mock-up.
/// The result is stored like an uploaded mock-up with the Lifestyle role, in the garment's artwork
/// group, so a lifestyle AI Shot can use it. It stays pending: the seller still reviews the generated
/// image at the Mockup Approval gate. The person photo itself is not stored.
/// </remarks>
public sealed class LifestyleMockupService(ICurrentUser user, IRepository<Product> products, IRepository<MockupImage> assets,
    IRepository<DesignImage> designs, IRepository<ApiUsageRecord> usage, IApiKeyRepository keys, IApiKeyCredentials credentials,
    IEnumerable<IImageGenerationProvider> imageProviders, IMediaStorage storage, IMockupAssetService mockups, IUnitOfWork uow, TimeProvider time,
    ILogger<LifestyleMockupService> logger, IRepository<MockupTemplate> templates, IMockupMapService maps,
    IPlanEntitlementService entitlements, ISystemApiKeyPool? systemKeys = null) : ILifestyleMockupService
{
    private readonly IImageGenerationProvider? ideogram = imageProviders.FirstOrDefault(provider => provider.Provider == ImageGenerationProviders.Ideogram);

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
        return await DressAsync(userId, product, garment, _ => Task.FromResult(personBytes), group: false, ideogram, ct);
    }

    public Task<Result<MockupResponse>> GenerateFromModelPhotoAsync(Guid productId, Guid garmentMockupId, Guid modelPhotoId, CancellationToken ct) =>
        FromModelPhotoAsync(productId, garmentMockupId, modelPhotoId, ideogram, ct);

    public Task<Result<MockupResponse>> GenerateForVideoAsync(Guid productId, Guid garmentMockupId, Guid modelPhotoId, CancellationToken ct) =>
        FromModelPhotoAsync(productId, garmentMockupId, modelPhotoId, ideogram, ct);

    private async Task<Result<MockupResponse>> FromModelPhotoAsync(Guid productId, Guid garmentMockupId, Guid modelPhotoId,
        IImageGenerationProvider? provider, CancellationToken ct)
    {
        var found = await FindAsync(productId, garmentMockupId, ct);
        if (found.IsFailure) return Result.Failure<MockupResponse>(found.Error);
        var (userId, product, garment) = found.Value;

        var model = await templates.Query().SingleOrDefaultAsync(item => item.Id == modelPhotoId && item.IsActive == true
            && item.Kind == MockupRules.ModelKind && (item.UserId == null || item.UserId == userId), ct);
        if (model is null) return Result.Failure<MockupResponse>(MockupErrors.ModelPhotoNotFound());

        // Read alongside the mock-up and the design, once the plan and the key have been checked.
        return await DressAsync(userId, product, garment, token => maps.DownloadAsync(model.BaseImageUrl, token),
            group: model.People == ModelPeople.Group, provider, ct);
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

    private async Task<Result<MockupResponse>> DressAsync(Guid userId, Product product, MockupImage garment,
        Func<CancellationToken, Task<byte[]>> readPerson, bool group, IImageGenerationProvider? images, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var productId = product.Id;
        if (images is null) return Result.Failure<MockupResponse>(Error.Failure("LifestyleGenerationFailed", "Lifestyle images are not available."));
        var name = images.Provider == ImageGenerationProviders.Gemini ? "Gemini" : "Ideogram";
        // On a plan with the platform's keys the image is made on those, within the plan's daily cap; the Seller's own
        // key, when they have one, only takes over once the platform's are used up for today.
        var platform = await PlatformKeysAsync(userId, images.Provider, ct);
        if (platform)
        {
            var cap = await entitlements.RequireDailyCapacityAsync(userId, PlanFeatures.DailyDesignImageCap, 1, ct);
            if (cap.IsFailure) return Result.Failure<MockupResponse>(cap.Error);
        }
        var key = (await keys.ListOwnedAsync(userId, ct)).FirstOrDefault(x =>
            x.ServiceProvider == images.Provider && x.IsActive == true && x.LastCheckSucceeded == true);
        if (key is null && !platform)
            return Result.Failure<MockupResponse>(Error.Conflict("MissingApiKey", $"Connect a valid {name} API key before generating a lifestyle image."));
        string? ownKey = null;
        if (key is not null)
        {
            try { ownKey = credentials.Unprotect(key.KeyValueEncrypted); }
            catch (CryptographicException) when (platform) { }
        }

        // An uploaded mock-up has no design: its own print is all there is to copy.
        var design = garment.DesignImageId is Guid designId && await designs.GetByIdAsync(designId, ct) is { DeletedAt: null } found ? found : null;

        var inputs = await ReadInputsAsync(readPerson, garment, design, ct);
        if (inputs.IsFailure) return Result.Failure<MockupResponse>(inputs.Error);
        var readTime = time.GetElapsedTime(started);

        // Ideogram keeps the result behind a link, which storage fetches itself; another provider sends the file back.
        var linked = images as IImageLinkProvider;
        List<ImageReference> sent = [new(inputs.Value.Person, MimeType(inputs.Value.Person)), new(inputs.Value.Garment, MimeType(inputs.Value.Garment))];
        if (inputs.Value.Design is { } artwork) sent.Add(new(artwork, "image/png"));
        var prompt = Prompt(design is not null, design is null ? null : product.DesiredDesignText, group);
        Task<ImageGenerationResult> Compose(string apiKey) => linked is not null
            ? linked.ComposeLinkAsync(apiKey, prompt, sent, ct)
            : images.ComposeAsync(apiKey, prompt, sent, ct);

        var composeStarted = time.GetTimestamp();
        ImageGenerationResult generated;
        try
        {
            generated = platform
                ? await systemKeys!.GenerateImageAsync(images.Provider, ownKey, Compose, ct)
                : await Compose(ownKey!);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(error, "Could not read mock-up {MockupId} for a lifestyle image", garment.Id);
            return Result.Failure<MockupResponse>(Error.Failure("LifestyleGenerationFailed", "Could not read the product mock-up. Try again."));
        }
        var composeTime = time.GetElapsedTime(composeStarted);
        if (!generated.IsSuccess)
        {
            logger.LogWarning("Lifestyle image for product {ProductId} was not generated: {Reason}", productId, generated.ErrorMessage);
            // Every platform key was used up or refused, and the Seller has no key of their own to go on with.
            if (platform && ownKey is null && generated.StopsJob)
                return Result.Failure<MockupResponse>(SystemKeysUsedUp(images.Provider));
            return Result.Failure<MockupResponse>(generated.IsBlocked
                ? Error.Validation($"{name} declined to edit this photo. Use a different photo.")
                : Error.Failure("LifestyleGenerationFailed", $"{name} could not generate the lifestyle image. Try again."));
        }

        var id = Guid.NewGuid();
        var now = time.GetUtcNow().UtcDateTime;
        var storeStarted = time.GetTimestamp();
        if (await StoreAsync(generated, linked, $"{id}.png", $"mockups/{userId}/{productId}/{id}", ct) is not { } saved)
            return Result.Failure<MockupResponse>(Error.Failure("LifestyleGenerationFailed", "Could not store the generated image. Try again."));
        var storeTime = time.GetElapsedTime(storeStarted);
        logger.LogInformation(
            "Lifestyle image {MockupId}: reading {ReadSeconds:0.0}s, {Provider} {ComposeSeconds:0.0}s, storing {StoreSeconds:0.0}s (from its link: {FromLink})",
            id, readTime.TotalSeconds, images.Provider, composeTime.TotalSeconds, storeTime.TotalSeconds, generated.ImageUrl is not null);

        await usage.AddAsync(new ApiUsageRecord
        {
            Id = Guid.NewGuid(), UserId = userId, ProductId = productId, Provider = images.Provider, Feature = UsageFeatures.Mockup,
            ModelName = generated.ModelUsed, RequestUnits = 1, CostUsd = generated.EstimatedCostUsd, Status = "success", CreatedAt = now,
            LatencyMs = (int)composeTime.TotalMilliseconds
        }, cancellationToken: ct);
        await assets.AddAsync(new MockupImage
        {
            Id = id, ProductId = productId, SourceType = MockupSources.Uploaded, StorageProvider = "cloudinary", StorageKey = saved.StorageKey,
            StorageVersion = saved.Version, ContentHash = saved.Hash, MockupWidthPx = saved.Width, MockupHeightPx = saved.Height,
            Role = "Lifestyle", ArtworkGroupKey = garment.ArtworkGroupKey ?? productId.ToString(), VariantKey = garment.VariantKey,
            MetadataRevision = MockupSources.FirstRevision, ApprovalStatus = "pending", Regions = WorkflowJson.Write(new MockupRegions()),
            IsFinal = false, CreatedAt = now,
            // The whole wait, from the request to the stored image; the provider's own part is the usage record's latency.
            GenerationTimeSeconds = Math.Round((decimal)time.GetElapsedTime(started).TotalSeconds, 1)
        }, cancellationToken: ct);
        await uow.SaveChangesAsync(ct);

        var listed = await mockups.ListAsync(productId, ct);
        return listed.IsFailure ? Result.Failure<MockupResponse>(listed.Error) : Result.Success(listed.Value.Single(x => x.Id == id));
    }

    private sealed record DressInputs(byte[] Person, byte[] Garment, byte[]? Design);

    // The person photo, the mock-up and the design are separate downloads, so they are read side by side.
    private async Task<Result<DressInputs>> ReadInputsAsync(Func<CancellationToken, Task<byte[]>> readPerson, MockupImage garment,
        DesignImage? design, CancellationToken ct)
    {
        async Task<byte[]> PersonAsync() => await readPerson(ct);
        async Task<byte[]> GarmentAsync() => garment.ContentHash != null
            ? await storage.ReadImageAsync(garment.StorageKey, garment.StorageVersion, ct)
            : await storage.ReadCompositeAsync(garment.MockupImageUrl!, ct);
        async Task<byte[]?> DesignAsync() => design is null ? null : await storage.ReadPublicImageAsync(design.StorageKey, ct);

        var person = PersonAsync();
        var garmentBytes = GarmentAsync();
        var artwork = DesignAsync();
        try
        {
            Task[] reading = [person, garmentBytes, artwork];
            await Task.WhenAll(reading);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException)
        {
            if (person.IsFaulted)
            {
                logger.LogWarning(error, "Could not read the person photo for a lifestyle image of mock-up {MockupId}", garment.Id);
                return Result.Failure<DressInputs>(Error.Failure("LifestyleGenerationFailed", "Could not read the model photo. Try again."));
            }
            logger.LogWarning(error, "Could not read mock-up {MockupId} for a lifestyle image", garment.Id);
            return Result.Failure<DressInputs>(Error.Failure("LifestyleGenerationFailed", "Could not read the product mock-up. Try again."));
        }
        return Result.Success(new DressInputs(await person, await garmentBytes, await artwork));
    }

    // Straight from the provider's link when it has one, so the image never passes through this server; the file is
    // sent instead when storage cannot fetch the link. Null when it could not be stored either way.
    private async Task<StoredMockup?> StoreAsync(ImageGenerationResult generated, IImageLinkProvider? linked, string fileName,
        string storageKey, CancellationToken ct)
    {
        var image = generated;
        if (generated.ImageUrl is { } link && linked is not null)
        {
            try
            {
                return await storage.UploadMockupFromUrlAsync(link, storageKey, ct);
            }
            catch (Exception error) when (error is InvalidOperationException or HttpRequestException)
            {
                logger.LogWarning(error, "Could not store lifestyle image {StorageKey} from its link; sending the file instead", storageKey);
            }
            image = await linked.DownloadLinkAsync(generated, ct);
            if (!image.IsSuccess) return null;
        }

        using var content = new MemoryStream(image.ImageBytes!);
        try
        {
            return await storage.UploadMockupAsync(content, fileName, storageKey, ct);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // Whether the Seller's plan has their images made on the platform's keys, and the platform has keys for the provider.
    private async Task<bool> PlatformKeysAsync(Guid userId, string provider, CancellationToken ct) =>
        systemKeys?.HasKeys(provider) == true && (await entitlements.GetAsync(userId, ct)).IsEnabled(PlanFeatures.SystemImageKeys);

    internal static Error SystemKeysUsedUp(string provider) =>
        Error.Failure("SystemImageKeysUsedUp", SystemApiKeyPoolExtensions.UsedUpMessage(provider));

    private static string MimeType(byte[] bytes) => bytes[0] == 0xff ? "image/jpeg" : bytes[0] == 137 ? "image/png" : "image/webp";
    private static Error Missing() => Error.NotFound("MockupNotFound", "Product or mockup not found.");
    private static Error TooLarge() => Error.Validation("The person photo must be at most 8 MB.");
}
