using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.BatchMockups;

/// <summary>
/// Model photos: photos of a person wearing a plain, light garment, which an existing mock-up dresses (its design
/// and garment color) without generative AI. They are mock-up templates of the 'model' kind, so compositing works
/// as for any template; only their garment is the part of the person the seller selected.
/// </summary>
public interface IModelPhotoService
{
    /// <summary>
    /// The model photos a seller can use for a product type: the shared library and their own, narrowed to who is in
    /// them when <paramref name="filter"/> says.
    /// </summary>
    Task<Result<IReadOnlyList<MockupTemplateResponseDto>>> ListAsync(string? productType, ModelPhotoFilter? filter = null, CancellationToken cancellationToken = default);

    /// <summary>The shared library of model photos, as an admin manages it.</summary>
    Task<Result<IReadOnlyList<MockupTemplateResponseDto>>> ListSystemAsync(ModelPhotoFilter? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// The person found in a photo that is not saved yet, for selecting the garment on it: the mask's alpha is
    /// the person.
    /// </summary>
    Task<Result<GarmentMaskPreviewResponseDto>> PreviewAsync(UploadFileDto? photo, CancellationToken cancellationToken = default);

    /// <summary>Saves a model photo; <paramref name="system"/> puts it in the shared library instead of the seller's own.</summary>
    Task<Result<MockupTemplateResponseDto>> CreateAsync(SaveModelPhotoRequestDto request, bool system, CancellationToken cancellationToken = default);

    /// <summary>Changes a model photo of the seller's own, or of the shared library when <paramref name="system"/>.</summary>
    Task<Result<MockupTemplateResponseDto>> UpdateAsync(Guid id, SaveModelPhotoRequestDto request, bool system, CancellationToken cancellationToken = default);

    /// <summary>Takes a model photo out of use. Mock-ups already made on it keep working.</summary>
    Task<Result> DeleteAsync(Guid id, bool system, CancellationToken cancellationToken = default);
}

public sealed class ModelPhotoService(
    ICurrentUser currentUser,
    IRepository<MockupTemplate> templates,
    IPublicImageService images,
    IMockupCompositor compositor,
    IMockupMapService mapService,
    IValidator<SaveModelPhotoRequestDto> validator,
    TimeProvider timeProvider,
    ILogger<ModelPhotoService> logger) : IModelPhotoService
{
    public async Task<Result<IReadOnlyList<MockupTemplateResponseDto>>> ListAsync(string? productType, ModelPhotoFilter? filter = null, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<IReadOnlyList<MockupTemplateResponseDto>>(MockupErrors.Unauthenticated("view"));

        var normalizedType = productType?.Trim().ToLowerInvariant();
        var items = await Matching(templates.Query(), filter)
            .Where(template => template.IsActive == true && template.Kind == MockupRules.ModelKind
                && (template.UserId == null || template.UserId == userId)
                && (string.IsNullOrEmpty(normalizedType) || template.ProductType.ToLower() == normalizedType))
            .OrderBy(template => template.Name)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<MockupTemplateResponseDto>>(items.Select(template => Map(template, userId)).ToList());
    }

    public async Task<Result<IReadOnlyList<MockupTemplateResponseDto>>> ListSystemAsync(ModelPhotoFilter? filter = null, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<IReadOnlyList<MockupTemplateResponseDto>>(MockupErrors.Unauthenticated("view"));

        var items = await Matching(templates.Query(), filter)
            .Where(template => template.IsActive == true && template.Kind == MockupRules.ModelKind && template.UserId == null)
            .OrderBy(template => template.ProductType)
            .ThenBy(template => template.Name)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<MockupTemplateResponseDto>>(items.Select(template => Map(template, userId)).ToList());
    }

    public async Task<Result<GarmentMaskPreviewResponseDto>> PreviewAsync(UploadFileDto? photo, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid)
            return Result.Failure<GarmentMaskPreviewResponseDto>(MockupErrors.Unauthenticated("preview"));
        if (!MockupImageValidators.IsSupportedImage(photo) || !MockupImageValidators.IsWithinSizeLimit(photo))
            return Result.Failure<GarmentMaskPreviewResponseDto>(MockupErrors.InvalidPreviewPhoto());
        if (!mapService.IsAvailable)
            return Result.Failure<GarmentMaskPreviewResponseDto>(MockupErrors.ProcessingUnavailable());

        var bytes = await ReadAllAsync(photo!.Content, cancellationToken);
        PreparedBasePhoto? person = null;
        if (MockupImageValidators.IsDirectlyReadable(bytes))
        {
            try
            {
                person = await mapService.PreviewModelPhotoAsync(bytes, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not find the person on a model photo.");
            }
        }

        // Without the person the garment can still be selected; it is just not kept from running onto the background.
        if (person is null)
            return Result.Success(new GarmentMaskPreviewResponseDto(false, "the person could not be found on this photo.", null));

        var found = MockupRules.IsSeparated(person.Stats);
        return Result.Success(new GarmentMaskPreviewResponseDto(
            found, found ? null : "the person could not be told apart from the background.",
            $"data:image/png;base64,{Convert.ToBase64String(person.MaskPng)}",
            person.WornGarmentPng is { } garment ? $"data:image/png;base64,{Convert.ToBase64String(garment)}" : null,
            person.GarmentCount));
    }

    public async Task<Result<MockupTemplateResponseDto>> CreateAsync(SaveModelPhotoRequestDto request, bool system, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.Unauthenticated("create"));

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<MockupTemplateResponseDto>(validation.ToValidationError());
        if (request.Photo is null)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.ModelPhotoRequired());
        if (request.GarmentSelection is null && !request.FindGarment)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.GarmentSelectionRequired());
        if (await IsNameTakenAsync(request.Name, null, cancellationToken))
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
        if (!mapService.IsAvailable)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.ProcessingUnavailable());

        var templateId = Guid.NewGuid();
        var storageKey = MockupRules.BaseImageKey(templateId);
        var photo = await ReadAllAsync(request.Photo.Content, cancellationToken);
        var selection = await SelectionOfAsync(request, cancellationToken);
        var position = PrintArea(request);

        // Checked before anything is stored, so a refused photo leaves nothing behind.
        var readable = MockupImageValidators.IsDirectlyReadable(photo);
        PreparedBasePhoto? prepared = null;
        if (readable)
        {
            var analyzed = await PrepareAsync(photo, selection, cancellationToken);
            if (analyzed.IsFailure)
                return Result.Failure<MockupTemplateResponseDto>(analyzed.Error);
            prepared = analyzed.Value;
            if (prepared is { WidthPx: > 0, HeightPx: > 0 } && !FitsWithin(position, prepared.WidthPx, prepared.HeightPx))
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.InvalidPrintArea());
        }

        var uploaded = await UploadPhotoAsync(request.Photo, photo, storageKey, cancellationToken);
        async Task<Result<MockupTemplateResponseDto>> RefuseAsync(Error error)
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);
            return Result.Failure<MockupTemplateResponseDto>(error);
        }

        if (!FitsWithin(position, uploaded.WidthPx, uploaded.HeightPx))
            return await RefuseAsync(MockupErrors.InvalidPrintArea());

        // A format the analysis can't read is analyzed from the JPEG the CDN makes of it.
        if (prepared is null)
        {
            var analyzed = await PrepareStoredAsync(uploaded.Url, selection, cancellationToken);
            if (analyzed.IsFailure)
                return await RefuseAsync(analyzed.Error);
            prepared = analyzed.Value;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var template = new MockupTemplate
        {
            Id = templateId,
            UserId = system ? null : userId,
            Name = request.Name.Trim(),
            ProductType = request.ProductType,
            BaseImageUrl = uploaded.Url,
            PrintAreaConfig = PrintAreaConfig(request, position),
            OutputWidthPx = uploaded.WidthPx,
            OutputHeightPx = uploaded.HeightPx,
            IsSystemTemplate = system,
            IsActive = true,
            UsageCount = 0,
            Kind = MockupRules.ModelKind,
            CreatedAt = now,
            UpdatedAt = now
        };
        // A photo saved without saying who is in it is taken as one person.
        ModelPeople.Apply(template, request.Attributes ?? new ModelAttributesDto(ModelPeople.Single));

        if (await StoreHelpersAsync(template, prepared, cancellationToken) is { } storeError)
            return await RefuseAsync(storeError);

        try
        {
            await templates.AddAsync(template, saveChange: true, cancellationToken);
        }
        catch (DbUpdateException)
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);
            if (await IsNameTakenAsync(request.Name, null, cancellationToken))
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
            throw;
        }
        catch
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);
            throw;
        }

        return Result.Success(Map(template, userId));
    }

    public async Task<Result<MockupTemplateResponseDto>> UpdateAsync(Guid id, SaveModelPhotoRequestDto request, bool system, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.Unauthenticated("update"));

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<MockupTemplateResponseDto>(validation.ToValidationError());

        var template = await templates.GetByIdAsync(id, cancellationToken);
        if (template is null || template.IsActive != true || !MockupRules.IsModelPhoto(template))
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.ModelPhotoNotFound());
        if (!CanChange(template, system, userId))
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.ModelPhotoNotOwner());
        // The garment selected before was drawn on the old photo.
        var newGarment = request.GarmentSelection is not null || request.FindGarment;
        if (request.Photo is not null && !newGarment)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.GarmentSelectionRequired());
        if (await IsNameTakenAsync(request.Name, id, cancellationToken))
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
        if (newGarment && !mapService.IsAvailable)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.ProcessingUnavailable());

        var position = PrintArea(request);
        var outputWidth = template.OutputWidthPx;
        var outputHeight = template.OutputHeightPx;
        // Set once a new photo is stored. A refused update removes it again; the photo in use is never touched.
        string? replacementKey = null;
        async Task<Result<MockupTemplateResponseDto>> RefuseAsync(Error error)
        {
            if (replacementKey is not null)
                await TryDeleteImageAsync(replacementKey, cancellationToken);
            return Result.Failure<MockupTemplateResponseDto>(error);
        }

        PreparedBasePhoto? prepared = null;
        if (newGarment)
        {
            var selection = await SelectionOfAsync(request, cancellationToken);
            if (request.Photo is not null)
            {
                var photo = await ReadAllAsync(request.Photo.Content, cancellationToken);
                var readable = MockupImageValidators.IsDirectlyReadable(photo);
                if (readable)
                {
                    var analyzed = await PrepareAsync(photo, selection, cancellationToken);
                    if (analyzed.IsFailure)
                        return Result.Failure<MockupTemplateResponseDto>(analyzed.Error);
                    prepared = analyzed.Value;
                    if (prepared is { WidthPx: > 0, HeightPx: > 0 } && !FitsWithin(position, prepared.WidthPx, prepared.HeightPx))
                        return Result.Failure<MockupTemplateResponseDto>(MockupErrors.InvalidPrintArea());
                }

                replacementKey = MockupRules.ReplacementImageKey(template.Id, timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                var uploaded = await UploadPhotoAsync(request.Photo, photo, replacementKey, cancellationToken);
                template.BaseImageUrl = uploaded.Url;
                outputWidth = uploaded.WidthPx;
                outputHeight = uploaded.HeightPx;
                if (!readable)
                {
                    var analyzed = await PrepareStoredAsync(uploaded.Url, selection, cancellationToken);
                    if (analyzed.IsFailure)
                        return await RefuseAsync(analyzed.Error);
                    prepared = analyzed.Value;
                }
            }
            else
            {
                var analyzed = await PrepareStoredAsync(template.BaseImageUrl, selection, cancellationToken);
                if (analyzed.IsFailure)
                    return Result.Failure<MockupTemplateResponseDto>(analyzed.Error);
                prepared = analyzed.Value;
            }
        }

        if (!FitsWithin(position, outputWidth, outputHeight))
            return await RefuseAsync(MockupErrors.InvalidPrintArea());

        if (prepared is not null && await StoreHelpersAsync(template, prepared, cancellationToken) is { } storeError)
            return await RefuseAsync(storeError);

        template.Name = request.Name.Trim();
        template.ProductType = request.ProductType;
        template.PrintAreaConfig = PrintAreaConfig(request, position);
        if (request.Attributes is not null)
            ModelPeople.Apply(template, request.Attributes);
        template.OutputWidthPx = outputWidth;
        template.OutputHeightPx = outputHeight;
        template.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            await templates.UpdateAsync(template, saveChange: true, cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await IsNameTakenAsync(request.Name, id, cancellationToken))
                return await RefuseAsync(MockupErrors.DuplicateName());
            throw;
        }

        return Result.Success(Map(template, userId));
    }

    public async Task<Result> DeleteAsync(Guid id, bool system, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure(MockupErrors.Unauthenticated("delete"));

        var template = await templates.GetByIdAsync(id, cancellationToken);
        if (template is null || template.IsActive != true || !MockupRules.IsModelPhoto(template))
            return Result.Failure(MockupErrors.ModelPhotoNotFound());
        if (!CanChange(template, system, userId))
            return Result.Failure(MockupErrors.ModelPhotoNotOwner());

        // Mock-ups made on it reference the row, so it is only taken out of use, as other templates are.
        template.IsActive = false;
        template.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await templates.UpdateAsync(template, saveChange: true, cancellationToken);
        return Result.Success();
    }

    // The photos that match a filter's attributes; a part left empty matches any.
    private static IQueryable<MockupTemplate> Matching(IQueryable<MockupTemplate> query, ModelPhotoFilter? filter)
    {
        var clean = ModelPeople.Clean(filter ?? ModelPhotoFilter.Any);
        if (clean.People is { } people) query = query.Where(template => template.People == people);
        if (clean.AgeGroup is { } age) query = query.Where(template => template.AgeGroup == age);
        if (clean.Gender is { } gender) query = query.Where(template => template.Gender == gender);
        if (clean.GroupType is { } group) query = query.Where(template => template.GroupType == group);
        return query;
    }

    // An admin changes the shared library; a seller only their own photos.
    // Corners count only when designs go in the print area; automatic placement measures the garments instead.
    private static IReadOnlyList<MockupPoint>? QuadOf(SaveModelPhotoRequestDto request) =>
        request.AutoPlacement ? null : request.PrintQuad is { Count: 4 } quad ? quad : null;

    // A print area marked by its corners is checked against the photo as the box around them.
    private static MockupPosition PrintArea(SaveModelPhotoRequestDto request) =>
        QuadOf(request) is { } quad ? MockupRules.QuadBounds(quad) : new MockupPosition(request.X, request.Y, request.Width, request.Height);

    private static string PrintAreaConfig(SaveModelPhotoRequestDto request, MockupPosition position) =>
        QuadOf(request) is { } quad ? MockupRules.SerializePrintQuad(quad) : MockupRules.SerializePrintArea(position, request.AutoPlacement);

    private static bool CanChange(MockupTemplate template, bool system, Guid userId) =>
        system ? template.UserId is null : template.UserId == userId;

    // The garment the seller selected, or null to find it automatically.
    private static async Task<byte[]?> SelectionOfAsync(SaveModelPhotoRequestDto request, CancellationToken cancellationToken) =>
        request.FindGarment || request.GarmentSelection is null ? null : await ReadAllAsync(request.GarmentSelection.Content, cancellationToken);

    private async Task<Result<PreparedBasePhoto>> PrepareAsync(byte[] photo, byte[]? selection, CancellationToken cancellationToken)
    {
        PreparedBasePhoto? prepared;
        try
        {
            prepared = await mapService.PrepareModelPhotoAsync(photo, selection, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not analyze a model photo.");
            return Result.Failure<PreparedBasePhoto>(MockupErrors.ModelPhotoUnusable("the photo or the garment selection could not be read."));
        }

        if (prepared is null)
            return Result.Failure<PreparedBasePhoto>(MockupErrors.GarmentNotFound());

        return MockupRules.ModelPhotoProblem(prepared.Stats) is { } problem
            ? Result.Failure<PreparedBasePhoto>(MockupErrors.ModelPhotoUnusable(problem))
            : Result.Success(prepared);
    }

    private async Task<Result<PreparedBasePhoto>> PrepareStoredAsync(string baseImageUrl, byte[]? selection, CancellationToken cancellationToken)
    {
        byte[] jpeg;
        try
        {
            jpeg = await mapService.DownloadAsync(baseImageUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not download model photo {Url}.", baseImageUrl);
            return Result.Failure<PreparedBasePhoto>(MockupErrors.ModelPhotoUnusable("the photo could not be read. Try again."));
        }

        return await PrepareAsync(jpeg, selection, cancellationToken);
    }

    // The garment is always recolored: the photo's garment passed the light-garment check, so its own color is white.
    private async Task<Error?> StoreHelpersAsync(MockupTemplate template, PreparedBasePhoto prepared, CancellationToken cancellationToken)
    {
        var version = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        try
        {
            await mapService.StoreHelpersAsync(MockupRules.HelperKeyPrefix(template.Id, version), prepared, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not store the print maps of model photo {TemplateId}.", template.Id);
            return MockupErrors.ModelPhotoUnusable("its print maps could not be stored. Try again.");
        }

        template.PrintMapsSourceUrl = template.BaseImageUrl;
        template.PrintMapsVersion = version;
        template.AllowRecolor = true;
        template.GarmentIsLight = true;
        template.GarmentColor = null;
        template.BackgroundRemoved = false;
        return null;
    }

    private MockupTemplateResponseDto Map(MockupTemplate template, Guid userId)
    {
        var mapsReady = MockupRules.HasCurrentMaps(template);
        return new(template.Id, template.Name, template.ProductType, template.BaseImageUrl, template.PreviewImageUrl,
            template.PrintAreaConfig, template.OutputWidthPx, template.OutputHeightPx, template.UsageCount ?? 0,
            template.IsSystemTemplate == true, template.UserId == userId, mapsReady, mapsReady && template.AllowRecolor,
            mapsReady ? compositor.BuildAssetUrl(template.BaseImageUrl, MockupRules.GarmentMaskKey(template)) : null,
            null, AllowBackground: false, Kind: MockupRules.ModelKind, Attributes: ModelPeople.Of(template), AiGenerated: template.AiGenerated);
    }

    private async Task<bool> IsNameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var candidate = name.Trim().ToLowerInvariant();
        return await templates.Query()
            .AnyAsync(template => template.Name.ToLower() == candidate && template.Id != excludeId, cancellationToken);
    }

    private static bool FitsWithin(MockupPosition position, int outputWidth, int outputHeight) =>
        position.X >= 0 && position.Y >= 0 && position.Width > 0 && position.Height > 0
        && position.X + position.Width <= outputWidth && position.Y + position.Height <= outputHeight;

    private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    // The upload's stream was already read, so the photo is sent from its bytes.
    private async Task<PublicImageUploadResult> UploadPhotoAsync(UploadFileDto upload, byte[] photo, string storageKey, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(photo);
        return await images.UploadImageWithMetadataAsync(new UploadFileDto(upload.FileName, upload.ContentType, stream.Length, stream), storageKey, cancellationToken);
    }

    private async Task TryDeleteImageAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await images.DeleteImageAsync(storageKey, cancellationToken);
        }
        catch (Exception)
        {
            // Best effort: the original failure must reach the caller unchanged.
        }
    }
}
