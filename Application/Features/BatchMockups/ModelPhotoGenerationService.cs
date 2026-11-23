using System.Security.Cryptography;
using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Features.Batches.Validators;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Application.Features.Entitlements;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.BatchMockups;

/// <summary>Makes a model photo with AI when the library has none of the people wanted.</summary>
public interface IModelPhotoGenerationService
{
    /// <summary>
    /// Has Ideogram make a photo of the people described in a plain white t-shirt, with the caller's Ideogram key (or the platform's), and
    /// saves it as a model photo: the seller's own, or the shared library's when <paramref name="system"/>.
    /// </summary>
    Task<Result<MockupTemplateResponseDto>> GenerateAsync(GenerateModelPhotoRequestDto request, bool system, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same, handing <paramref name="onDrawn"/> a temporary link to the photo as soon as Ideogram has drawn it, while
    /// it is still being saved. From then on a cancelled request no longer stops the save: the photo is paid for.
    /// </summary>
    Task<Result<MockupTemplateResponseDto>> GenerateAsync(GenerateModelPhotoRequestDto request, bool system, Func<string, Task> onDrawn, CancellationToken cancellationToken = default);
}

/// <remarks>
/// The photo is made once and kept, for mock-ups to be put on it. With <see cref="ModelPhotoSettings.PrintWithoutAi"/>
/// off that is done with AI and the photo is only stored; on, it is done without AI, so the print is exactly the design.
/// The prompt is fixed wording for the attributes chosen (<see cref="ModelPhotoPrompts"/>), never free text. The
/// garment is found automatically, as for an uploaded photo, and designs are placed by measuring it.
/// </remarks>
public sealed class ModelPhotoGenerationService(
    ICurrentUser currentUser,
    IModelPhotoService modelPhotos,
    IRepository<MockupTemplate> templates,
    IRepository<ApiUsageRecord> usage,
    IApiKeyRepository keys,
    IApiKeyCredentials credentials,
    IEnumerable<IImageGenerationProvider> imageProviders,
    IMockupMapService maps,
    TimeProvider time,
    ILogger<ModelPhotoGenerationService> logger,
    IPlanEntitlementService? entitlements = null,
    ISystemApiKeyPool? systemKeys = null) : IModelPhotoGenerationService
{
    // How long a photo already paid for may take to be saved once a request can no longer cancel it.
    private static readonly TimeSpan SaveTimeout = TimeSpan.FromMinutes(2);

    public Task<Result<MockupTemplateResponseDto>> GenerateAsync(GenerateModelPhotoRequestDto request, bool system, CancellationToken cancellationToken = default) =>
        MakeAsync(request, system, null, cancellationToken);

    public Task<Result<MockupTemplateResponseDto>> GenerateAsync(GenerateModelPhotoRequestDto request, bool system, Func<string, Task> onDrawn, CancellationToken cancellationToken = default) =>
        MakeAsync(request, system, onDrawn, cancellationToken);

    private async Task<Result<MockupTemplateResponseDto>> MakeAsync(GenerateModelPhotoRequestDto request, bool system, Func<string, Task>? onDrawn, CancellationToken cancellationToken)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.Unauthenticated("create"));
        if (!ProductRules.IsValidType(request.ProductType))
            return Result.Failure<MockupTemplateResponseDto>(Error.Validation("Choose a supported product type."));
        if (request.Attributes is null)
            return Result.Failure<MockupTemplateResponseDto>(Error.Validation("Choose who is in the photo."));
        if (ModelPeople.Problem(request.Attributes) is { } problem)
            return Result.Failure<MockupTemplateResponseDto>(Error.Validation(problem));
        var group = request.Attributes.People.Trim().ToLowerInvariant() == ModelPeople.Group;
        if (ModelPhotoStyles.Problem(request.ProductType, request.Style, group) is { } styleProblem)
            return Result.Failure<MockupTemplateResponseDto>(Error.Validation(styleProblem));
        var style = ModelPhotoStyles.Clean(request.Style);
        var productType = request.ProductType.Trim().ToLowerInvariant();
        if (imageProviders.FirstOrDefault(provider => provider.Provider == ImageGenerationProviders.Ideogram) is not { } ideogram)
            return Result.Failure<MockupTemplateResponseDto>(Error.Failure("ModelPhotoGenerationUnavailable", "Making model photos with AI is not available."));

        // On the platform's keys: an Admin making a photo for the shared library, or a Seller whose plan includes them
        // (within its daily cap). The caller's own key, when they have one, takes over once those are used up for today.
        var platform = await PlatformKeysAsync(userId, system, cancellationToken);
        if (platform && !system && entitlements is not null)
        {
            var cap = await entitlements.RequireDailyCapacityAsync(userId, PlanFeatures.DailyDesignImageCap, 1, cancellationToken);
            if (cap.IsFailure) return Result.Failure<MockupTemplateResponseDto>(cap.Error);
        }
        var key = (await keys.ListOwnedAsync(userId, cancellationToken)).FirstOrDefault(x =>
            x.ServiceProvider == ImageGenerationProviders.Ideogram && x.IsActive == true && x.LastCheckSucceeded == true);
        if (key is null && !platform)
            return Result.Failure<MockupTemplateResponseDto>(Error.Conflict("MissingApiKey", "Connect a valid Ideogram API key before making a model photo with AI."));

        // The people's looks are picked at random, so two photos with the same choices differ.
        var (prompt, aspectRatio) = ModelPhotoPrompts.Build(request.Attributes, productType, style, Random.Shared);
        // With no print maps to work out this server has no use for the photo itself: it stays at Ideogram until storage
        // fetches it.
        var linked = modelPhotos.GetOptions() is { PrintWithoutAi: false } ? ideogram as IImageLinkProvider : null;
        Task<ImageGenerationResult> Draw(string apiKey) => linked is not null
            ? linked.GenerateLinkAsync(apiKey, prompt, aspectRatio, cancellationToken: cancellationToken)
            : ideogram.GenerateAsync(apiKey, prompt, aspectRatio, cancellationToken: cancellationToken);
        ImageGenerationResult generated;
        var noOwnKey = false;
        try
        {
            string? ownKey = null;
            if (key is not null)
            {
                try { ownKey = credentials.Unprotect(key.KeyValueEncrypted); }
                catch (CryptographicException) when (platform) { }
            }
            noOwnKey = ownKey is null;
            generated = platform
                ? await systemKeys!.GenerateImageAsync(ImageGenerationProviders.Ideogram, ownKey, Draw, cancellationToken)
                : await Draw(ownKey!);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or CryptographicException)
        {
            logger.LogWarning(error, "A model photo could not be made with AI");
            return Result.Failure<MockupTemplateResponseDto>(Failed());
        }
        if (!generated.IsSuccess)
        {
            logger.LogWarning("A model photo was not made with AI: {Reason}", generated.ErrorMessage);
            // Every platform key was used up or refused, and the caller has no key of their own to go on with.
            if (platform && noOwnKey && generated.StopsJob)
                return Result.Failure<MockupTemplateResponseDto>(
                    Error.Failure("SystemImageKeysUsedUp", SystemApiKeyPoolExtensions.UsedUpMessage(ImageGenerationProviders.Ideogram)));
            return Result.Failure<MockupTemplateResponseDto>(generated.IsBlocked
                ? Error.Validation("Ideogram declined to make this photo. Try again, or choose other people.")
                : Failed());
        }

        // Ideogram has charged for it, whatever happens to the photo next. Once the person can see it, leaving the page no
        // longer stops it from being saved.
        using var saveTimeout = new CancellationTokenSource(SaveTimeout);
        var saving = onDrawn is not null && generated.ImageUrl is not null ? saveTimeout.Token : cancellationToken;
        var now = time.GetUtcNow().UtcDateTime;
        await usage.AddAsync(new ApiUsageRecord
        {
            Id = Guid.NewGuid(), UserId = userId, Provider = ImageGenerationProviders.Ideogram, Feature = "image_generation",
            ModelName = generated.ModelUsed, RequestUnits = 1, CostUsd = generated.EstimatedCostUsd, Status = "success", CreatedAt = now
        }, saveChange: true, saving);

        if (onDrawn is not null && generated.ImageUrl is { } drawn)
            await onDrawn(drawn);

        // Only one person on a plain backdrop takes the quick matte: on a group it can close the gap between two people's
        // garments, and they would then share one print.
        var quickMatte = !group && ModelPhotoStyles.IsStudio(style.Scene);
        var name = await FreeNameAsync(ModelPeople.Label(request.Attributes), saving);
        var created = await StoreAsync(generated, linked, new PhotoToSave(name, productType, request.Attributes, quickMatte, system), saving);
        if (created.IsFailure)
        {
            logger.LogWarning("A model photo made with AI could not be saved: {Reason}", created.Error.Message);
            return created;
        }

        // Marked as made by AI, with a print area on the chest for when the garment cannot be measured.
        var template = await templates.GetByIdAsync(created.Value.Id, saving);
        if (template is null)
            return created;
        template.AiGenerated = true;
        template.PrintAreaConfig = MockupRules.SerializePrintArea(ChestArea(template.OutputWidthPx, template.OutputHeightPx), automatic: true);
        template.UpdatedAt = now;
        await templates.UpdateAsync(template, saveChange: true, saving);
        return Result.Success(created.Value with { AiGenerated = true, PrintAreaConfig = template.PrintAreaConfig });
    }

    private sealed record PhotoToSave(string Name, string ProductType, ModelAttributesDto Attributes, bool QuickMatte, bool System);

    // Straight from Ideogram's link when it has one. If storage cannot read the link, the photo comes through this server.
    private async Task<Result<MockupTemplateResponseDto>> StoreAsync(ImageGenerationResult generated, IImageLinkProvider? linked,
        PhotoToSave photo, CancellationToken cancellationToken)
    {
        var image = generated;
        if (generated.ImageUrl is { } link && linked is not null)
        {
            var stored = await modelPhotos.CreateFromLinkAsync(
                new ModelPhotoLinkDto(photo.Name, photo.ProductType, link, photo.Attributes), photo.System, cancellationToken);
            if (stored.IsSuccess || stored.Error.Code != MockupErrors.PhotoLinkNotStoredCode)
                return stored;

            image = await linked.DownloadLinkAsync(generated, cancellationToken);
            if (!image.IsSuccess)
            {
                logger.LogWarning("A model photo made with AI could not be read back: {Reason}", image.ErrorMessage);
                return Result.Failure<MockupTemplateResponseDto>(NotSaved());
            }
        }

        // A 2K PNG photo can be over the upload limit; as a JPEG it is a fraction of it and looks the same.
        byte[] bytes;
        try
        {
            bytes = await maps.ToJpegAsync(image.ImageBytes!, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(error, "The photo made with AI could not be read");
            return Result.Failure<MockupTemplateResponseDto>(Failed());
        }
        using var content = new MemoryStream(bytes);
        return await modelPhotos.CreateAsync(new SaveModelPhotoRequestDto(
            photo.Name, photo.ProductType, 0, 0, 1, 1,
            new UploadFileDto("model.jpg", "image/jpeg", bytes.Length, content),
            GarmentSelection: null, AutoPlacement: true, FindGarment: true, Attributes: photo.Attributes,
            PlainBackdrop: photo.QuickMatte),
            photo.System, cancellationToken);
    }

    // The middle of the photo's upper half, where a chest print sits on a person framed head to mid-thigh.
    internal static MockupPosition ChestArea(int width, int height) =>
        new((int)(width * 0.35), (int)(height * 0.3), Math.Max(1, (int)(width * 0.3)), Math.Max(1, (int)(height * 0.25)));

    /// <summary>
    /// Who is in the photo ("Teen · Female", "Family"), numbered from 2 when that name is taken: names are unique, and
    /// a code or an "AI" mark would only clutter them (the card says it was made with AI).
    /// </summary>
    internal async Task<string> FreeNameAsync(string label, CancellationToken cancellationToken)
    {
        var lower = label.ToLowerInvariant();
        var taken = (await templates.Query()
                .Where(template => template.Name.ToLower().StartsWith(lower))
                .Select(template => template.Name.ToLower())
                .ToListAsync(cancellationToken))
            .ToHashSet();
        if (!taken.Contains(lower))
            return label;
        var number = 2;
        while (taken.Contains($"{lower} {number}"))
            number++;
        return $"{label} {number}";
    }

    // Whether the photo is made on the platform's Ideogram keys: the platform has some, and the photo is for the shared
    // library or the Seller's plan includes them.
    private async Task<bool> PlatformKeysAsync(Guid userId, bool system, CancellationToken cancellationToken) =>
        systemKeys?.HasKeys(ImageGenerationProviders.Ideogram) == true
        && (system || (entitlements is not null
            && (await entitlements.GetAsync(userId, cancellationToken)).IsEnabled(PlanFeatures.SystemImageKeys)));

    private static Error Failed() => Error.Failure("ModelPhotoGenerationFailed", "Ideogram could not make the photo. Try again.");

    private static Error NotSaved() => Error.Failure("ModelPhotoNotSaved", "The photo was made but could not be saved. Try again.");
}
