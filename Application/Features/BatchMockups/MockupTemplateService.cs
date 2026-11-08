using System.Text.Json;
using System.Text.Json.Nodes;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.BatchMockups.Common;
using APCS.Application.Features.BatchMockups.Dtos.Request;
using APCS.Application.Features.BatchMockups.Dtos.Response;
using APCS.Application.Features.DesignGeneration.Common;
using APCS.Application.Features.Entitlements;
using APCS.Common.Constants;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.BatchMockups;

/// <summary>Lists mock-up templates, stores per-batch mock-up selection, and composites mock-up images.</summary>
public sealed class MockupTemplateService(
    ICurrentUser currentUser,
    IRepository<MockupTemplate> templates,
    IRepository<BatchJob> batches,
    IRepository<BatchJobProduct> rows,
    IRepository<Product> products,
    IRepository<DesignImage> designImages,
    IRepository<MockupImage> mockupImages,
    IPublicImageService images,
    IMockupCompositor compositor,
    IMockupMapService mapService,
    IUnitOfWork unitOfWork,
    IValidator<ApplyMockupTemplatesRequestDto> applyValidator,
    IValidator<CreateMockupTemplateRequestDto> createValidator,
    IValidator<UpdateMockupTemplateRequestDto> updateValidator,
    IValidator<GenerateMockupImageRequestDto> generateValidator,
    IValidator<SaveMockupOverrideRequestDto> overrideValidator,
    IValidator<SaveMockupOverridesRequestDto> overridesValidator,
    TimeProvider timeProvider,
    ILogger<MockupTemplateService> logger,
    IPlanEntitlementService entitlements) : IMockupTemplateService
{
    public async Task<Result<IReadOnlyList<MockupTemplateResponseDto>>> ListAsync(string? productType, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<IReadOnlyList<MockupTemplateResponseDto>>(MockupErrors.Unauthenticated("view"));

        var normalizedType = productType?.Trim().ToLowerInvariant();

        var items = await templates.Query()
            .Where(template => template.IsActive == true
                && template.Kind != MockupRules.ModelKind
                && (template.UserId == null || template.UserId == userId)
                && (string.IsNullOrEmpty(normalizedType) || template.ProductType.ToLower() == normalizedType))
            .OrderByDescending(template => template.UsageCount)
            .ThenBy(template => template.Name)
            .ToListAsync(cancellationToken);

        // Another side of a template is listed with it, not on its own.
        var byId = items.ToDictionary(template => template.Id);
        return Result.Success<IReadOnlyList<MockupTemplateResponseDto>>(items
            .Where(template => !MockupRules.IsOtherView(template))
            .Select(template => Map(template, userId, SidesOf(template, byId)))
            .ToList());
    }

    public async Task<Result<BatchMockupSelectionResponseDto>> GetSelectionAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.Unauthenticated("view"));

        var batch = await FindOwnedBatchAsync(batchJobId, cancellationToken);
        if (batch is null)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.BatchNotFound());

        return Result.Success(new BatchMockupSelectionResponseDto(
            batch.Id, ReadSelection(batch.Config), ReadColors(batch.Config), ReadTemplateColors(batch.Config), ReadTemplateBackgrounds(batch.Config),
            MockupRules.ReadAreaSources(batch.Config)));
    }

    public async Task<Result<BatchMockupSelectionResponseDto>> ApplyAsync(Guid batchJobId, ApplyMockupTemplatesRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.Unauthenticated("update"));

        var validation = await applyValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<BatchMockupSelectionResponseDto>(validation.ToValidationError());
        }

        var batch = await FindOwnedBatchAsync(batchJobId, cancellationToken);
        if (batch is null)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.BatchNotFound());
        // Selection is plain config read when mock-ups are generated, so it can change before or after
        // image generation — only not while a run is in flight.
        if (BatchJobStatuses.Active.Contains(batch.Status.ToLowerInvariant()))
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.JobGenerating());

        var candidates = await templates.Query()
            .Where(template => request.TemplateIds.Contains(template.Id) && template.IsActive == true && template.Kind != MockupRules.ModelKind)
            .ToListAsync(cancellationToken);

        if (candidates.Count != request.TemplateIds.Count)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.TemplateNotFound());

        var batchTypes = await BatchProductTypesAsync(batchJobId, cancellationToken);
        var incompatible = batchTypes.Count > 0
            ? candidates.FirstOrDefault(template => !batchTypes.Contains(template.ProductType.ToLowerInvariant()))
            : null;
        if (incompatible is not null)
            return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.IncompatibleTemplate(incompatible.Name));

        var colors = (request.GarmentColors ?? []).Select(color => color.ToUpperInvariant()).ToList();
        var colorsByTemplate = NormalizeTemplateColors(request.TemplateColors);
        var backgrounds = NormalizeTemplateBackgrounds(request.TemplateBackgrounds);
        // The cut mask is made now, so the mock-ups made later are plain URLs like every other one. A template's other
        // sides are put on the same background.
        var withBackground = candidates.Where(template => backgrounds.ContainsKey(template.Id)).ToList();
        withBackground.AddRange(await SidesAsync(withBackground, null, cancellationToken));
        foreach (var template in withBackground)
        {
            if (!await TryEnsureCutMaskAsync(template, cancellationToken))
                return Result.Failure<BatchMockupSelectionResponseDto>(MockupErrors.BackgroundNotAllowed(template.Name));
        }

        // What a print area prints is kept only for areas the template still has.
        var areaSources = (request.AreaSources ?? new Dictionary<Guid, IReadOnlyDictionary<string, MockupAreaSourceDto>>())
            .Select(entry => (entry.Key, Areas: candidates.FirstOrDefault(template => template.Id == entry.Key) is { } template
                && MockupRules.ParsePrintLayout(template.PrintAreaConfig) is { } layout
                    ? (IReadOnlyDictionary<string, MockupAreaSourceDto>)entry.Value
                        .Where(area => layout.Areas.Any(own => own.Key == area.Key))
                        .ToDictionary(area => area.Key, area => area.Value with { DesignImageId = area.Value.Kind == MockupRules.SourceDesign ? area.Value.DesignImageId : null })
                    : new Dictionary<string, MockupAreaSourceDto>()))
            .Where(entry => entry.Areas.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Areas);

        batch.Config = WriteSelection(batch.Config, request.TemplateIds, colors, colorsByTemplate, backgrounds, areaSources);
        batch.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await batches.UpdateAsync(batch, saveChange: true, cancellationToken);

        return Result.Success(new BatchMockupSelectionResponseDto(batch.Id, request.TemplateIds, colors, colorsByTemplate, backgrounds, areaSources));
    }

    public async Task<Result<MockupTemplateResponseDto>> CreateAsync(CreateMockupTemplateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.Unauthenticated("create"));

        var planCheck = await entitlements.RequireFeatureAsync(userId, PlanFeatures.CustomMockupTemplates, cancellationToken);
        if (planCheck.IsFailure) return Result.Failure<MockupTemplateResponseDto>(planCheck.Error);

        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<MockupTemplateResponseDto>(validation.ToValidationError());
        }

        MockupTemplate? main = null;
        if (request.MainTemplateId is Guid mainId)
        {
            var found = await FindMainForSideAsync(mainId, userId, cancellationToken);
            if (found.IsFailure) return Result.Failure<MockupTemplateResponseDto>(found.Error);
            main = found.Value;
            if (!string.Equals(request.ProductType, main.ProductType, StringComparison.OrdinalIgnoreCase))
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.TemplateNotForProduct(request.Name));
        }

        // A side is never listed on its own, so a taken name is not an error for it: SideNameAsync picks a free one.
        if (main is null && await IsNameTakenAsync(request.Name, null, cancellationToken))
        {
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
        }

        if (!mapService.IsAvailable)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.ProcessingUnavailable());

        var templateId = Guid.NewGuid();
        var storageKey = StorageKey(templateId);
        var photo = await ReadAllAsync(request.BaseImage!.Content, cancellationToken);
        var readable = MockupImageValidators.IsDirectlyReadable(photo);
        var prepared = readable ? await TryPrepareAsync(photo, cancellationToken) : null;
        var areas = RequestedAreas(request.Areas, new MockupPosition(request.X, request.Y, request.Width, request.Height), null);
        // Checked against the analyzed photo first, so a rejected request leaves nothing stored.
        if (prepared is { WidthPx: > 0, HeightPx: > 0 } && !AreasFit(areas, prepared.WidthPx, prepared.HeightPx))
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.InvalidPrintArea());

        var uploaded = await UploadPhotoAsync(request.BaseImage, photo, storageKey, cancellationToken);

        if (!AreasFit(areas, uploaded.WidthPx, uploaded.HeightPx))
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.InvalidPrintArea());
        }

        if (!readable)
            prepared = await TryPrepareStoredAsync(uploaded.Url, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var template = new MockupTemplate
        {
            Id = templateId,
            UserId = userId,
            Name = main is null ? request.Name.Trim() : await SideNameAsync(request.Name, templateId, cancellationToken),
            ProductType = request.ProductType,
            BaseImageUrl = uploaded.Url,
            PrintAreaConfig = MockupRules.SerializePrintLayout(request.View, areas),
            OutputWidthPx = uploaded.WidthPx,
            OutputHeightPx = uploaded.HeightPx,
            IsSystemTemplate = false,
            IsActive = true,
            UsageCount = 0,
            Kind = MockupRules.GarmentKind,
            CreatedAt = now,
            UpdatedAt = now
        };

        var recolorError = await ApplyPreparedAsync(template, prepared, request.AllowRecolor, cancellationToken)
            ?? ApplyGarmentColor(template, request.GarmentColor);
        if (recolorError is not null)
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);
            return Result.Failure<MockupTemplateResponseDto>(recolorError);
        }

        // A side and its link on the template are saved together, so a failure never leaves a side on its own.
        if (main is not null)
            await LinkViewAsync(main, request.View!, template, now, cancellationToken);

        try
        {
            await templates.AddAsync(template, saveChange: main is null, cancellationToken);
            if (main is not null)
                await templates.UpdateAsync(main, saveChange: true, cancellationToken);
        }
        catch (DbUpdateException)
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);

            if (main is null && await IsNameTakenAsync(request.Name, null, cancellationToken))
            {
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
            }

            throw;
        }
        catch
        {
            await TryDeleteImageAsync(storageKey, cancellationToken);
            throw;
        }

        return Result.Success(Map(template, userId));
    }

    public async Task<Result<MockupTemplateResponseDto>> UpdateAsync(Guid id, UpdateMockupTemplateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.Unauthenticated("update"));

        var planCheck = await entitlements.RequireFeatureAsync(userId, PlanFeatures.CustomMockupTemplates, cancellationToken);
        if (planCheck.IsFailure) return Result.Failure<MockupTemplateResponseDto>(planCheck.Error);

        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<MockupTemplateResponseDto>(validation.ToValidationError());
        }

        var template = await templates.GetByIdAsync(id, cancellationToken);
        // A model photo is changed through its own endpoints: its garment is a selection, not what the model finds.
        if (template is null || template.IsActive != true || MockupRules.IsModelPhoto(template))
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.TemplateNotFoundSingle());
        if (template.UserId is null || template.UserId != userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.UpdateNotOwner());

        if (!MockupRules.IsOtherView(template) && await IsNameTakenAsync(request.Name, id, cancellationToken))
        {
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.DuplicateName());
        }

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

        // The helper images are derived once per photo: again only for a new photo, or when they are missing.
        var reprocess = request.BaseImage is not null || !MockupRules.HasCurrentMaps(template);
        if (reprocess && !mapService.IsAvailable)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.ProcessingUnavailable());

        byte[]? photo = null;
        if (request.BaseImage is not null)
            photo = await ReadAllAsync(request.BaseImage.Content, cancellationToken);
        else if (reprocess)
            photo = await TryDownloadAsync(template.BaseImageUrl, cancellationToken);

        var current = MockupRules.ParsePrintLayout(template.PrintAreaConfig);
        var areas = RequestedAreas(request.Areas, new MockupPosition(request.X, request.Y, request.Width, request.Height), current);
        var view = request.View ?? current?.View;
        PreparedBasePhoto? prepared = null;
        if (photo is not null)
        {
            // A photo that was not uploaded just now is the JPEG the CDN keeps of it, which is always read.
            var readable = request.BaseImage is null || MockupImageValidators.IsDirectlyReadable(photo);
            if (readable)
                prepared = await TryPrepareAsync(photo, cancellationToken);
            // Checked before anything is stored, so a rejected request does not replace the photo.
            if (prepared is { WidthPx: > 0, HeightPx: > 0 } && !AreasFit(areas, prepared.WidthPx, prepared.HeightPx))
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.InvalidPrintArea());

            if (request.BaseImage is not null)
            {
                replacementKey = MockupRules.ReplacementImageKey(template.Id, timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
                var uploaded = await UploadPhotoAsync(request.BaseImage, photo, replacementKey, cancellationToken);
                template.BaseImageUrl = uploaded.Url;
                outputWidth = uploaded.WidthPx;
                outputHeight = uploaded.HeightPx;
                if (!readable)
                    prepared = await TryPrepareStoredAsync(uploaded.Url, cancellationToken);
            }
        }

        if (!AreasFit(areas, outputWidth, outputHeight))
        {
            return await RefuseAsync(MockupErrors.InvalidPrintArea());
        }

        if (photo is not null)
        {
            if (await ApplyPreparedAsync(template, prepared, request.AllowRecolor, cancellationToken) is { } recolorError)
                return await RefuseAsync(recolorError);
        }
        else
        {
            if (request.AllowRecolor && !template.GarmentIsLight)
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.NotRecolorable("the garment is too dark or could not be separated from the background."));
            template.AllowRecolor = request.AllowRecolor;
        }

        if (ApplyGarmentColor(template, request.GarmentColor) is { } colorError)
            return await RefuseAsync(colorError);

        template.Name = MockupRules.IsOtherView(template) ? await SideNameAsync(request.Name, template.Id, cancellationToken) : request.Name.Trim();
        template.ProductType = request.ProductType;
        template.PrintAreaConfig = MockupRules.KeepViewLinks(template.PrintAreaConfig, MockupRules.SerializePrintLayout(view, areas));
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
            {
                return await RefuseAsync(MockupErrors.DuplicateName());
            }

            throw;
        }

        return Result.Success(Map(template, userId));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure(MockupErrors.Unauthenticated("delete"));

        var template = await templates.GetByIdAsync(id, cancellationToken);
        if (template is null || template.IsActive != true || MockupRules.IsModelPhoto(template))
            return Result.Failure(MockupErrors.TemplateNotFoundSingle());
        if (template.UserId is null || template.UserId != userId)
            return Result.Failure(MockupErrors.DeleteNotOwner());

        // No deleted_at column on mockup_templates, and product_mockup_templates/mockup_images both
        // FK-reference this row — deactivating (matching the IsActive filter ListAsync already uses)
        // is the safe option instead of a hard delete.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Its other sides can't be used without it.
        foreach (var side in await SidesAsync([template], userId, cancellationToken))
        {
            side.IsActive = false;
            side.UpdatedAt = now;
            await templates.UpdateAsync(side, cancellationToken: cancellationToken);
        }
        template.IsActive = false;
        template.UpdatedAt = now;
        await templates.UpdateAsync(template, saveChange: true, cancellationToken);

        return Result.Success();
    }

    public async Task<Result<MockupTemplateResponseDto>> SetViewAsync(Guid id, string view, Guid? viewTemplateId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.Unauthenticated("update"));

        var planCheck = await entitlements.RequireFeatureAsync(userId, PlanFeatures.CustomMockupTemplates, cancellationToken);
        if (planCheck.IsFailure) return Result.Failure<MockupTemplateResponseDto>(planCheck.Error);
        if (!MockupRules.OtherViews.Contains(view))
            return Result.Failure<MockupTemplateResponseDto>(MockupErrors.UnknownView());

        var found = await FindMainForSideAsync(id, userId, cancellationToken);
        if (found.IsFailure) return Result.Failure<MockupTemplateResponseDto>(found.Error);
        var main = found.Value;

        var views = MockupRules.ViewTemplateIds(main.PrintAreaConfig);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        MockupTemplate? side = null;
        if (viewTemplateId is Guid sideId)
        {
            side = await templates.GetByIdAsync(sideId, cancellationToken);
            if (side is null || side.IsActive != true || MockupRules.IsModelPhoto(side) || side.Id == main.Id || side.UserId != userId)
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.TemplateNotFoundSingle());
            // A side is a photo of the same garment, and belongs to one template only.
            if (MockupRules.ViewTemplateIds(side.PrintAreaConfig).Count > 0
                || MockupRules.MainTemplateId(side.PrintAreaConfig) is Guid otherMain && otherMain != main.Id
                || views.Any(entry => entry.Value == side.Id && entry.Key != view))
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.ViewTaken());
            if (!string.Equals(side.ProductType, main.ProductType, StringComparison.OrdinalIgnoreCase))
                return Result.Failure<MockupTemplateResponseDto>(MockupErrors.TemplateNotForProduct(side.Name));
        }

        await LinkViewAsync(main, view, side, now, cancellationToken);
        if (side is not null)
            await templates.UpdateAsync(side, cancellationToken: cancellationToken);
        await templates.UpdateAsync(main, saveChange: true, cancellationToken);

        var sides = await SidesAsync([main], userId, cancellationToken);
        return Result.Success(Map(main, userId, sides));
    }

    // The user's template that another side can be added to: an active garment photo of its own front.
    private async Task<Result<MockupTemplate>> FindMainForSideAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var main = await templates.GetByIdAsync(id, cancellationToken);
        if (main is null || main.IsActive != true || MockupRules.IsModelPhoto(main) || MockupRules.IsOtherView(main))
            return Result.Failure<MockupTemplate>(MockupErrors.TemplateNotFoundSingle());
        if (main.UserId is null || main.UserId != userId)
            return Result.Failure<MockupTemplate>(MockupErrors.UpdateNotOwner());
        return Result.Success(main);
    }

    // Puts the side in the template's view (none takes the view off). The side it replaces can't be used on its own,
    // so it is turned off. Nothing is saved: the caller saves the side and the template.
    private async Task LinkViewAsync(MockupTemplate main, string view, MockupTemplate? side, DateTime now, CancellationToken cancellationToken)
    {
        var views = new Dictionary<string, Guid>(MockupRules.ViewTemplateIds(main.PrintAreaConfig));
        if (views.TryGetValue(view, out var replacedId) && replacedId != side?.Id
            && await templates.GetByIdAsync(replacedId, cancellationToken) is { IsActive: true } replaced
            && MockupRules.MainTemplateId(replaced.PrintAreaConfig) == main.Id)
        {
            replaced.IsActive = false;
            replaced.UpdatedAt = now;
            await templates.UpdateAsync(replaced, cancellationToken: cancellationToken);
        }

        if (side is null)
            views.Remove(view);
        else
        {
            views[view] = side.Id;
            side.PrintAreaConfig = MockupRules.WithViewLinks(MockupRules.WithView(side.PrintAreaConfig, view), null, main.Id);
            side.UpdatedAt = now;
        }
        main.PrintAreaConfig = MockupRules.WithViewLinks(main.PrintAreaConfig, views, null);
        main.UpdatedAt = now;
    }

    // The other sides of these templates that can still be used.
    private async Task<List<MockupTemplate>> SidesAsync(IReadOnlyCollection<MockupTemplate> mains, Guid? userId, CancellationToken cancellationToken)
    {
        var sideIds = mains.SelectMany(template => MockupRules.ViewTemplateIds(template.PrintAreaConfig).Values)
            .Except(mains.Select(template => template.Id)).Distinct().ToList();
        if (sideIds.Count == 0)
            return [];
        var mainIds = mains.Select(template => template.Id).ToHashSet();
        return (await templates.Query()
                .Where(template => sideIds.Contains(template.Id) && template.IsActive == true && template.Kind != MockupRules.ModelKind
                    && (userId == null || template.UserId == null || template.UserId == userId))
                .ToListAsync(cancellationToken))
            .Where(template => MockupRules.MainTemplateId(template.PrintAreaConfig) is Guid mainId && mainIds.Contains(mainId))
            .ToList();
    }

    // A template's other sides among those already loaded, in the order front, back, left, right.
    private static List<MockupTemplate> SidesOf(MockupTemplate main, IReadOnlyDictionary<Guid, MockupTemplate> loaded) =>
        MockupRules.ViewTemplateIds(main.PrintAreaConfig)
            .OrderBy(entry => MockupRules.OtherViews.ToList().IndexOf(entry.Key))
            .Select(entry => loaded.GetValueOrDefault(entry.Value))
            .OfType<MockupTemplate>()
            .Where(side => MockupRules.MainTemplateId(side.PrintAreaConfig) == main.Id)
            .ToList();

    public async Task<Result<MockupImageResponseDto>> GenerateCompositeAsync(Guid designImageId, GenerateMockupImageRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.Unauthenticated("generate"));

        var planCheck = await entitlements.RequireFeatureAsync(userId, PlanFeatures.TemplateMockups, cancellationToken);
        if (planCheck.IsFailure) return Result.Failure<MockupImageResponseDto>(planCheck.Error);
        // Compositing runs on the platform's Cloudinary account, so it counts toward the daily safety cap.
        var capCheck = await entitlements.RequireDailyCapacityAsync(userId, PlanFeatures.DailyMockupCap, cancellationToken: cancellationToken);
        if (capCheck.IsFailure) return Result.Failure<MockupImageResponseDto>(capCheck.Error);
        var allowance = await entitlements.RequireAllowanceAsync(userId, UsageAllowances.Mockups, cancellationToken: cancellationToken);
        if (allowance.IsFailure) return Result.Failure<MockupImageResponseDto>(allowance.Error);

        var validation = await generateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure<MockupImageResponseDto>(validation.ToValidationError());
        }

        var designImage = await designImages.Query()
            .SingleOrDefaultAsync(image => image.Id == designImageId && image.DeletedAt == null, cancellationToken);
        if (designImage is null)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.DesignImageNotFound());

        var product = await products.Query()
            .SingleOrDefaultAsync(item => item.Id == designImage.ProductId, cancellationToken);
        if (product is null || product.UserId != userId)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.DesignImageNotFound());

        var template = await templates.Query()
            .SingleOrDefaultAsync(item => item.Id == request.MockupTemplateId && item.IsActive == true && item.Kind != MockupRules.ModelKind
                && (item.UserId == null || item.UserId == userId), cancellationToken);
        if (template is null)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.TemplateNotFoundSingle());
        if (!string.Equals(template.ProductType, product.ProductType, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<MockupImageResponseDto>(MockupErrors.IncompatibleTemplate(template.Name));

        var layout = MockupRules.ParsePrintLayout(template.PrintAreaConfig);
        if (layout is null)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.InvalidPrintArea());
        if (request.Areas?.Any(area => layout.Areas.All(own => own.Key != area.Key)) == true)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.UnknownPrintArea());
        if (request.Areas?.Any(area => !MockupRules.AreaFits(area, template.OutputWidthPx, template.OutputHeightPx)) == true)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.InvalidPrintArea());

        var custom = request.Areas is not null
            ? new MockupOverrideDto(template.Id, null, null, null, Areas: request.Areas)
            : request.X is not null
                ? new MockupOverrideDto(template.Id, null, null, new MockupPrintAreaDto(request.X.Value, request.Y!.Value, request.Width!.Value, request.Height!.Value))
                : null;
        var resolved = MockupRules.ResolveAreas(layout, custom, null);

        // Another design printed in an area must be one of the seller's own.
        var otherIds = resolved.Where(area => area.Source == MockupRules.SourceDesign).Select(area => area.DesignImageId!.Value)
            .Where(id => id != designImage.Id).Distinct().ToList();
        var others = new Dictionary<Guid, DesignImage> { [designImage.Id] = designImage };
        if (otherIds.Count > 0)
        {
            var found = await designImages.Query()
                .Where(image => otherIds.Contains(image.Id) && image.DeletedAt == null)
                .ToListAsync(cancellationToken);
            var ownerIds = found.Select(image => image.ProductId).Distinct().ToList();
            var owned = await products.Query()
                .Where(item => ownerIds.Contains(item.Id) && item.UserId == userId)
                .Select(item => item.Id)
                .ToListAsync(cancellationToken);
            foreach (var image in found.Where(image => owned.Contains(image.ProductId)))
                others[image.Id] = image;
            if (otherIds.Any(id => !others.ContainsKey(id)))
                return Result.Failure<MockupImageResponseDto>(MockupErrors.AreaDesignNotFound());
        }

        var placements = Placements(resolved, designImage, others);
        if (placements.Count == 0)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.NothingToPrint());

        var layers = MockupRules.Layers(template, designImage, request.GarmentColor);
        if (request.GarmentColor is not null && layers.GarmentColor is null)
            return Result.Failure<MockupImageResponseDto>(MockupErrors.RecolorNotAllowed(template.Name));

        string compositeUrl;
        try
        {
            compositeUrl = Compose(template.BaseImageUrl, designImage, placements, layers);
        }
        catch (InvalidOperationException)
        {
            // Seeded/system templates can carry a placeholder BaseImageUrl (no real Cloudinary
            // "/upload/" segment) — surface that as a normal error instead of an unhandled 500.
            return Result.Failure<MockupImageResponseDto>(MockupErrors.TemplateNotUsable(template.Name));
        }

        // Made exactly as one that already exists (the job's own, or an earlier preview), it is that one: a second
        // copy would later be added to the video sources beside it.
        var same = await mockupImages.Query()
            .FirstOrDefaultAsync(image => image.DesignImageId == designImage.Id && image.MockupTemplateId == template.Id
                && image.MockupImageUrl == compositeUrl && image.DeletedAt == null, cancellationToken);
        if (same is not null)
            return Result.Success(MapImage(same));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var mockupImage = new MockupImage
        {
            Id = Guid.NewGuid(),
            DesignImageId = designImage.Id,
            ProductId = product.Id,
            MockupTemplateId = template.Id,
            StorageProvider = "cloudinary",
            // Informational only: the composite is a Cloudinary-rendered transformation URL, not a
            // separately uploaded asset, so there is no real public id to store here.
            StorageKey = $"mockups/{template.Id:N}/{designImage.Id:N}",
            MockupImageUrl = compositeUrl,
            MockupWidthPx = template.OutputWidthPx,
            MockupHeightPx = template.OutputHeightPx,
            GenerationTimeSeconds = 0,
            ApprovalStatus = "pending",
            BatchJobProductId = designImage.BatchJobProductId,
            GarmentColor = layers.GarmentColor,
            SourceType = MockupSources.Generated,
            MetadataRevision = MockupSources.FirstRevision,
            Role = MockupSources.RoleForView(layout.View),
            Regions = MockupSources.NoRegions,
            ArtworkGroupKey = MockupSources.ArtworkGroupKey(designImage.Id),
            VariantKey = layers.GarmentColor,
            CreatedAt = now
        };

        await mockupImages.AddAsync(mockupImage, cancellationToken: cancellationToken);
        template.UsageCount = (template.UsageCount ?? 0) + 1;
        template.UpdatedAt = now;
        await templates.UpdateAsync(template, saveChange: true, cancellationToken);

        return Result.Success(MapImage(mockupImage));
    }

    public async Task<Result<GenerateAllMockupsResultDto>> GenerateAllAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.Unauthenticated("generate"));

        var batch = await FindOwnedBatchAsync(batchJobId, cancellationToken);
        if (batch is null)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.BatchNotFound());
        if (string.Equals(batch.Status, BatchJobStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.JobNotReady());

        var planCheck = await entitlements.RequireFeatureAsync(userId, PlanFeatures.TemplateMockups, cancellationToken);
        if (planCheck.IsFailure) return Result.Failure<GenerateAllMockupsResultDto>(planCheck.Error);
        var capCheck = await entitlements.RequireDailyCapacityAsync(userId, PlanFeatures.DailyMockupCap, cancellationToken: cancellationToken);
        if (capCheck.IsFailure) return Result.Failure<GenerateAllMockupsResultDto>(capCheck.Error);

        return await GenerateAllCoreAsync(batch, userId, cancellationToken);
    }

    public async Task<Result<GenerateAllMockupsResultDto>> GetJobMockupsAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.Unauthenticated("view"));

        var batch = await FindOwnedBatchAsync(batchJobId, cancellationToken);
        if (batch is null)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.BatchNotFound());
        // Nothing can have been made yet: an answer, not an error, since this is asked when a page opens.
        if (string.Equals(batch.Status, BatchJobStatuses.Draft, StringComparison.OrdinalIgnoreCase) || ReadSelection(batch.Config).Count == 0)
            return Result.Success(new GenerateAllMockupsResultDto(0, 0, 0, [], []));

        return await GenerateAllCoreAsync(batch, userId, cancellationToken, create: false);
    }

    public async Task<Result<GenerateAllMockupsResultDto>> GenerateAllForJobAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        var batch = await batches.Query()
            .SingleOrDefaultAsync(job => job.Id == batchJobId && job.DeletedAt == null, cancellationToken);
        if (batch is null)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.BatchNotFound());
        if (batch.Status is not (BatchJobStatuses.Completed or BatchJobStatuses.PartiallyCompleted))
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.JobNotReady());
        // The Seller reviews the designs first and then asks for the mock-ups themselves.
        if (ReadRequireApproval(batch.Config) == true)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.ApprovalPending());

        return await GenerateAllCoreAsync(batch, batch.UserId, cancellationToken);
    }

    // With `create` off nothing is written: the result holds only the mock-ups that already exist and are up to date.
    private async Task<Result<GenerateAllMockupsResultDto>> GenerateAllCoreAsync(BatchJob batch, Guid userId, CancellationToken cancellationToken, bool create = true)
    {
        var batchJobId = batch.Id;
        var selectedIds = ReadSelection(batch.Config);
        if (selectedIds.Count == 0)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.NoTemplatesSelected());
        var selectedColors = ReadColors(batch.Config);
        var colorsByTemplate = ReadTemplateColors(batch.Config);
        var backgrounds = ReadTemplateBackgrounds(batch.Config);
        var overrides = ReadOverrides(batch.Config);
        var areaSources = MockupRules.ReadAreaSources(batch.Config);

        var selectedTemplates = await templates.Query()
            .Where(template => selectedIds.Contains(template.Id) && template.IsActive == true && template.Kind != MockupRules.ModelKind
                && (template.UserId == null || template.UserId == userId))
            .ToListAsync(cancellationToken);
        // A template with photos of other sides makes a mock-up of each side too, in the same colors and on the same
        // backgrounds as its front.
        var sides = await SidesAsync(selectedTemplates, userId, cancellationToken);
        if (sides.Count > 0)
        {
            var ownColors = new Dictionary<Guid, IReadOnlyList<string>>(colorsByTemplate);
            var ownBackgrounds = new Dictionary<Guid, IReadOnlyDictionary<string, string>>(backgrounds);
            foreach (var side in sides)
            {
                var mainId = MockupRules.MainTemplateId(side.PrintAreaConfig)!.Value;
                if (colorsByTemplate.TryGetValue(mainId, out var mainColors)) ownColors[side.Id] = mainColors;
                if (backgrounds.TryGetValue(mainId, out var mainBackgrounds)) ownBackgrounds[side.Id] = mainBackgrounds;
            }
            colorsByTemplate = ownColors;
            backgrounds = ownBackgrounds;
            selectedTemplates.AddRange(sides);
        }
        var shownTemplateIds = selectedTemplates.Select(template => template.Id).ToHashSet();
        // A mock-up made its own way may use a template the job did not select.
        var otherTemplateIds = overrides.Values.Select(item => item.MockupTemplateId).Where(id => !shownTemplateIds.Contains(id)).Distinct().ToList();
        var otherTemplates = otherTemplateIds.Count == 0 ? [] : await templates.Query()
            .Where(template => otherTemplateIds.Contains(template.Id) && template.IsActive == true && template.Kind != MockupRules.ModelKind
                && (template.UserId == null || template.UserId == userId))
            .ToListAsync(cancellationToken);
        var templatesById = selectedTemplates.Concat(otherTemplates).ToDictionary(template => template.Id);

        var rowIds = await rows.Query()
            .Where(row => row.BatchJobId == batchJobId)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        var candidateImages = await designImages.Query()
            .Where(image => image.BatchJobProductId != null && rowIds.Contains(image.BatchJobProductId!.Value) && image.DeletedAt == null)
            .ToListAsync(cancellationToken);

        List<DesignImage> targetImages;
        int noDesignImageCount;
        var noApprovedImageCount = 0;
        if (ReadRequireApproval(batch.Config) is null)
        {
            // A job started before approval existed: one image per product row, the lowest variation
            // index (then earliest), standing in for "the approved one".
            targetImages = candidateImages
                .GroupBy(image => image.BatchJobProductId!.Value)
                .Select(group => group.OrderBy(image => image.VariationIndex).ThenBy(image => image.CreatedAt).First())
                .ToList();
            noDesignImageCount = rowIds.Count - targetImages.Count;
        }
        else
        {
            // Every approved image gets its mock-ups, so a product with several approved variations has several.
            targetImages = candidateImages
                .Where(image => image.ApprovalStatus == ApprovalStatuses.Approved)
                .OrderBy(image => image.VariationIndex)
                .ThenBy(image => image.CreatedAt)
                .ToList();
            var rowsWithImages = candidateImages.Select(image => image.BatchJobProductId!.Value).Distinct().Count();
            noDesignImageCount = rowIds.Count - rowsWithImages;
            noApprovedImageCount = rowsWithImages - targetImages.Select(image => image.BatchJobProductId!.Value).Distinct().Count();
        }

        // A print area may print another design of the job, chosen for the job or for one mock-up; it must still be one of the job's.
        var areaDesignIds = areaSources.Values.SelectMany(byArea => byArea.Values)
            .Concat(overrides.Values.SelectMany(item => item.Areas ?? []).Select(area => new MockupAreaSourceDto(area.Source ?? "", area.DesignImageId)))
            .Where(source => source.Kind == MockupRules.SourceDesign && source.DesignImageId is not null)
            .Select(source => source.DesignImageId!.Value)
            .ToHashSet();
        var areaDesigns = candidateImages.Where(image => areaDesignIds.Contains(image.Id)).ToDictionary(image => image.Id);

        var productIds = targetImages.Select(image => image.ProductId).Distinct().ToList();
        var productsById = (await products.Query().Where(product => productIds.Contains(product.Id)).ToListAsync(cancellationToken))
            .ToDictionary(product => product.Id);

        var designImageIds = targetImages.Select(image => image.Id).ToList();
        var knownTemplateIds = templatesById.Keys.ToHashSet();
        // One that was removed is never shown again, even when a mock-up is made exactly the same way once more.
        var existingImages = await mockupImages.Query()
            .Where(image => image.DesignImageId != null && designImageIds.Contains(image.DesignImageId.Value)
                && image.MockupTemplateId != null && knownTemplateIds.Contains(image.MockupTemplateId.Value) && image.DeletedAt == null)
            .ToListAsync(cancellationToken);
        // Keyed by the composite URL too: the URL encodes the template's base photo and print area,
        // so a row made before the template was edited no longer matches and gets regenerated.
        var existingByKey = existingImages
            .GroupBy(image => (image.DesignImageId, image.MockupTemplateId, image.MockupImageUrl))
            .ToDictionary(group => group.Key, group => group.First());

        if (create)
        {
            // Free makes a few mockups per run, and Free and the Pro trial count every new one. At most
            // this many are made: one per approved design, chosen template and colour.
            var planned = targetImages.Count * selectedTemplates.Sum(template =>
            {
                var palette = colorsByTemplate.TryGetValue(template.Id, out var ownColors) ? ownColors : selectedColors;
                return template.AllowRecolor && MockupRules.HasCurrentMaps(template) && palette.Count > 0 ? palette.Count : 1;
            });
            var plan = await entitlements.GetAsync(userId, cancellationToken);
            if (plan.MaxMockupsPerRun >= 0 && planned > plan.MaxMockupsPerRun)
                return Result.Failure<GenerateAllMockupsResultDto>(PlanErrors.MockupsPerRunExceeded(plan.MaxMockupsPerRun, planned));
            if (planned - existingImages.Count is var newMockups and > 0)
            {
                var allowance = await entitlements.RequireAllowanceAsync(userId, UsageAllowances.Mockups, newMockups, cancellationToken);
                if (allowance.IsFailure) return Result.Failure<GenerateAllMockupsResultDto>(allowance.Error);
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var created = new List<MockupImage>();
        var current = new List<(MockupImage Image, string SlotKey, MockupOverrideDto? Override)>();
        // A template whose mock-ups could not be made this time keeps the ones made before.
        var failedTemplateIds = new HashSet<Guid>();
        var removed = new List<RemovedMockupSlotDto>();
        var errors = new List<string>();
        var usageIncrements = new Dictionary<Guid, int>();
        var noCompatibleTemplateCount = 0;

        void AddError(string message)
        {
            if (errors.Count < 50 && !errors.Contains(message)) errors.Add(message);
        }

        foreach (var designImage in targetImages)
        {
            if (!productsById.TryGetValue(designImage.ProductId, out var product))
                continue;

            var matchingTemplates = selectedTemplates
                .Where(template => string.Equals(template.ProductType, product.ProductType, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matchingTemplates.Count == 0)
            {
                noCompatibleTemplateCount++;
                continue;
            }

            foreach (var template in matchingTemplates)
            {
                var layout = MockupRules.ParsePrintLayout(template.PrintAreaConfig);
                if (layout is null)
                {
                    AddError($"{template.Name}: invalid print area.");
                    continue;
                }

                // A recolorable template gets one mock-up per color picked for the batch, else its own
                // color; any other template keeps the photo's color.
                var recolorable = template.AllowRecolor && MockupRules.HasCurrentMaps(template);
                // The colors chosen for this template; templates without any of their own share the batch's list.
                var palette = colorsByTemplate.TryGetValue(template.Id, out var ownColors) ? ownColors : selectedColors;
                var colors = recolorable && palette.Count > 0
                    ? palette.Cast<string?>().ToList()
                    : [recolorable ? template.GarmentColor : null];

                foreach (var color in colors)
                {
                    var slotKey = MockupRules.SlotKey(designImage.Id, template.Id, color);
                    var custom = overrides.GetValueOrDefault(slotKey);
                    if (custom is { Removed: true })
                    {
                        removed.Add(new(slotKey, product.Id, designImage.Id, template.Id, template.Name, color));
                        continue;
                    }

                    // Made from the job's settings, or from the ones chosen for this mock-up alone.
                    var made = (Template: template, Color: color, Layout: layout,
                        Background: MockupRules.ResolveBackground(backgrounds.GetValueOrDefault(template.Id), color));
                    if (custom is not null)
                    {
                        if (templatesById.TryGetValue(custom.MockupTemplateId, out var chosen)
                            && string.Equals(chosen.ProductType, product.ProductType, StringComparison.OrdinalIgnoreCase)
                            && MockupRules.ParsePrintLayout(chosen.PrintAreaConfig) is { } chosenLayout)
                        {
                            made = (chosen, custom.GarmentColor, chosenLayout, custom.BackgroundColor);
                        }
                        else
                        {
                            AddError($"A mock-up of {product.Name} was set up on a template that can't be used any more, so it is made with the job's settings.");
                            custom = null;
                        }
                    }

                    var resolved = MockupRules.ResolveAreas(made.Layout, custom, areaSources.GetValueOrDefault(made.Template.Id));
                    var placements = Placements(resolved, designImage, areaDesigns,
                        () => AddError($"A design chosen for a print area of {made.Template.Name} is no longer in this job, so that area was left empty."));
                    if (placements.Count == 0)
                    {
                        AddError($"{made.Template.Name}: no print area prints a design, so no mock-up was made on it.");
                        continue;
                    }

                    var layers = MockupRules.Layers(made.Template, designImage, made.Color, made.Background);
                    string compositeUrl;
                    try
                    {
                        compositeUrl = Compose(made.Template.BaseImageUrl, designImage, placements, layers);
                    }
                    catch (InvalidOperationException)
                    {
                        failedTemplateIds.Add(made.Template.Id);
                        AddError($"{made.Template.Name}: does not have a usable base photo yet.");
                        // Every color of the job's template fails the same way; a mock-up of its own only fails itself.
                        if (custom is null) break;
                        continue;
                    }

                    if (existingByKey.TryGetValue((designImage.Id, made.Template.Id, compositeUrl), out var upToDate))
                    {
                        current.Add((upToDate, slotKey, custom));
                        continue;
                    }

                    if (!create)
                        continue;

                    var mockupImage = new MockupImage
                    {
                        Id = Guid.NewGuid(),
                        DesignImageId = designImage.Id,
                        ProductId = product.Id,
                        MockupTemplateId = made.Template.Id,
                        StorageProvider = "cloudinary",
                        StorageKey = $"mockups/{made.Template.Id:N}/{designImage.Id:N}",
                        MockupImageUrl = compositeUrl,
                        MockupWidthPx = made.Template.OutputWidthPx,
                        MockupHeightPx = made.Template.OutputHeightPx,
                        GenerationTimeSeconds = 0,
                        ApprovalStatus = "pending",
                        BatchJobProductId = designImage.BatchJobProductId,
                        GarmentColor = layers.GarmentColor,
                        BackgroundColor = layers.BackgroundColor,
                        SourceType = MockupSources.Generated,
                        MetadataRevision = MockupSources.FirstRevision,
                        Role = MockupSources.RoleForView(made.Layout.View),
                        Regions = MockupSources.NoRegions,
                        ArtworkGroupKey = MockupSources.ArtworkGroupKey(designImage.Id),
                        VariantKey = layers.GarmentColor,
                        CreatedAt = now
                    };
                    created.Add(mockupImage);
                    current.Add((mockupImage, slotKey, custom));
                    usageIncrements[made.Template.Id] = usageIncrements.GetValueOrDefault(made.Template.Id) + 1;
                }
            }
        }

        foreach (var mockupImage in created)
            await mockupImages.AddAsync(mockupImage, cancellationToken: cancellationToken);

        foreach (var template in templatesById.Values)
        {
            if (!usageIncrements.TryGetValue(template.Id, out var increment))
                continue;
            template.UsageCount = (template.UsageCount ?? 0) + increment;
            template.UpdatedAt = now;
            await templates.UpdateAsync(template, cancellationToken: cancellationToken);
        }

        var retiredCount = create ? await RetireOutdatedAsync(designImageIds, current.Select(item => item.Image.Id).ToHashSet(), failedTemplateIds, now, cancellationToken) : 0;

        if (created.Count > 0 || usageIncrements.Count > 0 || retiredCount > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        var allImages = current.Select(item => MapImage(item.Image) with { SlotKey = item.SlotKey, Override = item.Override }).ToList();

        return Result.Success(new GenerateAllMockupsResultDto(
            created.Count, noDesignImageCount, noCompatibleTemplateCount, errors, allImages, noApprovedImageCount, removed));
    }

    public async Task<Result<GenerateAllMockupsResultDto>> SaveOverrideAsync(Guid batchJobId, SaveMockupOverrideRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.Unauthenticated("edit"));

        var validation = await overrideValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<GenerateAllMockupsResultDto>(validation.ToValidationError());

        return await SaveOverridesCoreAsync(batchJobId, userId, [request], cancellationToken);
    }

    public async Task<Result<GenerateAllMockupsResultDto>> SaveOverridesAsync(Guid batchJobId, SaveMockupOverridesRequestDto request, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.Unauthenticated("edit"));

        var validation = await overridesValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<GenerateAllMockupsResultDto>(validation.ToValidationError());

        return await SaveOverridesCoreAsync(batchJobId, userId, request.Overrides, cancellationToken);
    }

    // Checks every change against its mock-up's product and the template it names, keeps all of them or none, and
    // makes the job's mock-ups once.
    private async Task<Result<GenerateAllMockupsResultDto>> SaveOverridesCoreAsync(Guid batchJobId, Guid userId,
        IReadOnlyList<SaveMockupOverrideRequestDto> changes, CancellationToken cancellationToken)
    {
        var found = await FindEditableSlotsAsync(batchJobId, changes.Select(change => change.SlotKey).ToList(), cancellationToken);
        if (found.IsFailure)
            return Result.Failure<GenerateAllMockupsResultDto>(found.Error);
        var (batch, productsBySlot) = found.Value;

        // Changes copied across a job name the same few templates: each is looked up, and its cut mask made sure of, once.
        var templatesById = new Dictionary<Guid, MockupTemplate?>();
        var cutMaskByTemplate = new Dictionary<Guid, bool>();
        var overrides = new Dictionary<string, MockupOverrideDto>(ReadOverrides(batch.Config));
        foreach (var change in changes)
        {
            var kept = await CheckOverrideAsync(batch.Id, change.Override, productsBySlot[change.SlotKey], userId, templatesById, cutMaskByTemplate, cancellationToken);
            if (kept.IsFailure)
                return Result.Failure<GenerateAllMockupsResultDto>(kept.Error);
            overrides[change.SlotKey] = kept.Value;
        }

        var replaced = changes.Select(change => change.ReplacedMockupImageId).OfType<Guid>().ToList();
        return await StoreOverridesAndRegenerateAsync(batch, userId, overrides, replaced, cancellationToken);
    }

    // The change as it is kept, when the mock-up's product and the template it names can take it.
    private async Task<Result<MockupOverrideDto>> CheckOverrideAsync(Guid batchJobId, MockupOverrideDto requested, Product product, Guid userId,
        Dictionary<Guid, MockupTemplate?> templatesById, Dictionary<Guid, bool> cutMaskByTemplate, CancellationToken cancellationToken)
    {
        if (requested.Removed)
            return Result.Success(new MockupOverrideDto(requested.MockupTemplateId, null, null, null, Removed: true));

        if (!templatesById.TryGetValue(requested.MockupTemplateId, out var template))
        {
            template = await templates.Query()
                .SingleOrDefaultAsync(item => item.Id == requested.MockupTemplateId && item.IsActive == true && item.Kind != MockupRules.ModelKind
                    && (item.UserId == null || item.UserId == userId), cancellationToken);
            templatesById[requested.MockupTemplateId] = template;
        }
        if (template is null)
            return Result.Failure<MockupOverrideDto>(MockupErrors.TemplateNotFoundSingle());
        if (!string.Equals(template.ProductType, product.ProductType, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<MockupOverrideDto>(MockupErrors.TemplateNotForProduct(template.Name));
        if (requested.GarmentColor is not null && !(template.AllowRecolor && MockupRules.HasCurrentMaps(template)))
            return Result.Failure<MockupOverrideDto>(MockupErrors.RecolorNotAllowed(template.Name));
        if (requested.BackgroundColor is not null)
        {
            if (!cutMaskByTemplate.TryGetValue(template.Id, out var canCut))
                cutMaskByTemplate[template.Id] = canCut = await TryEnsureCutMaskAsync(template, cancellationToken);
            if (!canCut)
                return Result.Failure<MockupOverrideDto>(MockupErrors.BackgroundNotAllowed(template.Name));
        }
        if (requested.PrintArea is { } area
            && !FitsWithin(new MockupPosition(area.X, area.Y, area.Width, area.Height), template.OutputWidthPx, template.OutputHeightPx))
            return Result.Failure<MockupOverrideDto>(MockupErrors.InvalidPrintArea());
        if (requested.Areas is { } areas)
        {
            var layout = MockupRules.ParsePrintLayout(template.PrintAreaConfig);
            if (layout is null || areas.Any(item => layout.Areas.All(own => own.Key != item.Key)))
                return Result.Failure<MockupOverrideDto>(MockupErrors.UnknownPrintArea());
            if (!AreasFit(areas, template.OutputWidthPx, template.OutputHeightPx))
                return Result.Failure<MockupOverrideDto>(MockupErrors.InvalidPrintArea());
            // Another design printed in an area must be one of the same product's in this job: the product's listing is
            // written for its own designs only.
            var chosenIds = areas.Where(item => item.Source == MockupRules.SourceDesign).Select(item => item.DesignImageId!.Value).Distinct().ToList();
            if (chosenIds.Count > 0)
            {
                var rowIds = await rows.Query().Where(row => row.BatchJobId == batchJobId && row.ProductId == product.Id)
                    .Select(row => row.Id).ToListAsync(cancellationToken);
                var found = await designImages.Query()
                    .CountAsync(image => chosenIds.Contains(image.Id) && image.DeletedAt == null
                        && image.BatchJobProductId != null && rowIds.Contains(image.BatchJobProductId.Value), cancellationToken);
                if (found != chosenIds.Count)
                    return Result.Failure<MockupOverrideDto>(MockupErrors.AreaDesignNotFound());
            }
        }

        return Result.Success(requested with
        {
            GarmentColor = requested.GarmentColor?.ToUpperInvariant(),
            BackgroundColor = requested.BackgroundColor?.ToUpperInvariant(),
            // The areas say where the main one goes, so the older single print area is not kept beside them.
            PrintArea = requested.Areas is null ? requested.PrintArea : null,
            Areas = requested.Areas?.Select(item => item with
            {
                Name = null,
                Rotation = MockupRules.NormalizeRotation(item.Rotation),
                DesignImageId = item.Source == MockupRules.SourceDesign ? item.DesignImageId : null,
            }).ToList(),
        });
    }

    public async Task<Result<GenerateAllMockupsResultDto>> ResetOverrideAsync(Guid batchJobId, string slotKey, Guid? replacedMockupImageId, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<GenerateAllMockupsResultDto>(MockupErrors.Unauthenticated("edit"));

        var found = await FindEditableSlotsAsync(batchJobId, [slotKey], cancellationToken);
        if (found.IsFailure)
            return Result.Failure<GenerateAllMockupsResultDto>(found.Error);
        var batch = found.Value.Batch;

        var overrides = new Dictionary<string, MockupOverrideDto>(ReadOverrides(batch.Config));
        overrides.Remove(slotKey);
        return await StoreOverridesAndRegenerateAsync(batch, userId, overrides,
            replacedMockupImageId is Guid replacedId ? [replacedId] : [], cancellationToken);
    }

    // What the job's settings no longer make (a template taken out, a color dropped, a template photo or print area edited
    // since, a mock-up changed on its own, an earlier preview) goes. Otherwise "Add this product's mock-ups" would bring
    // such older versions in beside the current ones. One already among the video sources stays: it may be approved or in
    // use, so the seller decides about it at Mockup Approval.
    private async Task<int> RetireOutdatedAsync(IReadOnlyCollection<Guid> designImageIds, IReadOnlySet<Guid> currentIds,
        IReadOnlySet<Guid> failedTemplateIds, DateTime now, CancellationToken cancellationToken)
    {
        var outdated = (await mockupImages.Query()
                .Where(image => image.DesignImageId != null && designImageIds.Contains(image.DesignImageId.Value) && image.DeletedAt == null
                    && image.SourceType == MockupSources.Generated && image.ContentHash == null && image.ApprovalStatus != ApprovalStatuses.Approved)
                .ToListAsync(cancellationToken))
            .Where(image => !currentIds.Contains(image.Id) && !(image.MockupTemplateId is Guid templateId && failedTemplateIds.Contains(templateId)))
            .ToList();
        foreach (var image in outdated)
        {
            image.DeletedAt = now;
            await mockupImages.UpdateAsync(image, cancellationToken: cancellationToken);
        }
        return outdated.Count;
    }

    // The job, and the product of each mock-up the slot keys name, when the job can have its mock-ups changed now.
    private async Task<Result<(BatchJob Batch, IReadOnlyDictionary<string, Product> Products)>> FindEditableSlotsAsync(Guid batchJobId,
        IReadOnlyCollection<string> slotKeys, CancellationToken cancellationToken)
    {
        var batch = await FindOwnedBatchAsync(batchJobId, cancellationToken);
        if (batch is null)
            return Result.Failure<(BatchJob, IReadOnlyDictionary<string, Product>)>(MockupErrors.BatchNotFound());
        if (BatchJobStatuses.Active.Contains(batch.Status.ToLowerInvariant()))
            return Result.Failure<(BatchJob, IReadOnlyDictionary<string, Product>)>(MockupErrors.JobGenerating());
        if (string.Equals(batch.Status, BatchJobStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<(BatchJob, IReadOnlyDictionary<string, Product>)>(MockupErrors.JobNotReady());

        // Only mock-ups the job's settings make: designs of this job on its selected templates.
        var selection = ReadSelection(batch.Config);
        HashSet<Guid>? sideIds = null;
        var designBySlot = new Dictionary<string, Guid>();
        foreach (var slotKey in slotKeys)
        {
            if (!MockupRules.TryReadSlotKey(slotKey, out var designImageId, out var templateId))
                return Result.Failure<(BatchJob, IReadOnlyDictionary<string, Product>)>(MockupErrors.SlotNotFound());
            if (!selection.Contains(templateId))
            {
                // Another side of a selected template makes mock-ups of the job too.
                sideIds ??= (await templates.Query().Where(template => selection.Contains(template.Id)).Select(template => template.PrintAreaConfig).ToListAsync(cancellationToken))
                    .SelectMany(config => MockupRules.ViewTemplateIds(config).Values).ToHashSet();
                if (!sideIds.Contains(templateId))
                    return Result.Failure<(BatchJob, IReadOnlyDictionary<string, Product>)>(MockupErrors.SlotNotFound());
            }
            designBySlot[slotKey] = designImageId;
        }

        var rowIds = await rows.Query().Where(row => row.BatchJobId == batch.Id).Select(row => row.Id).ToListAsync(cancellationToken);
        var designIds = designBySlot.Values.Distinct().ToList();
        var designs = await designImages.Query()
            .Where(image => designIds.Contains(image.Id) && image.DeletedAt == null
                && image.BatchJobProductId != null && rowIds.Contains(image.BatchJobProductId.Value))
            .ToListAsync(cancellationToken);
        var productIds = designs.Select(image => image.ProductId).Distinct().ToList();
        var productsById = (await products.Query().Where(item => productIds.Contains(item.Id)).ToListAsync(cancellationToken))
            .ToDictionary(item => item.Id);

        var productsBySlot = new Dictionary<string, Product>();
        foreach (var (slotKey, designImageId) in designBySlot)
        {
            var design = designs.FirstOrDefault(image => image.Id == designImageId);
            if (design is null || !productsById.TryGetValue(design.ProductId, out var product))
                return Result.Failure<(BatchJob, IReadOnlyDictionary<string, Product>)>(MockupErrors.SlotNotFound());
            productsBySlot[slotKey] = product;
        }
        return Result.Success<(BatchJob, IReadOnlyDictionary<string, Product>)>((batch, productsBySlot));
    }

    private async Task<Result<GenerateAllMockupsResultDto>> StoreOverridesAndRegenerateAsync(BatchJob batch, Guid userId,
        IReadOnlyDictionary<string, MockupOverrideDto> overrides, IReadOnlyCollection<Guid> replacedMockupImageIds, CancellationToken cancellationToken)
    {
        batch.Config = WriteOverrides(batch.Config, overrides);
        batch.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await batches.UpdateAsync(batch, saveChange: true, cancellationToken);

        var result = await GenerateAllCoreAsync(batch, userId, cancellationToken);
        if (result.IsFailure)
            return result;
        var goneIds = replacedMockupImageIds.Where(id => result.Value.Images.All(image => image.Id != id)).ToList();
        if (goneIds.Count == 0)
            return result;

        // A mock-up shown until now that is not one of the job's any more goes, unless it was already added to the
        // video sources: there it may be approved or in use, so the seller decides about it at Mockup Approval.
        var designImageIds = result.Value.Images.Select(image => image.DesignImageId)
            .Concat(result.Value.Removed?.Select(slot => slot.DesignImageId) ?? []).ToHashSet();
        var replaced = await mockupImages.Query()
            .Where(image => goneIds.Contains(image.Id) && image.DeletedAt == null && image.ContentHash == null
                && image.SourceType == MockupSources.Generated && image.DesignImageId != null && designImageIds.Contains(image.DesignImageId.Value))
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var image in replaced)
        {
            image.DeletedAt = now;
            await mockupImages.UpdateAsync(image, saveChange: true, cancellationToken);
        }
        return result;
    }

    private static MockupImageResponseDto MapImage(MockupImage mockupImage) =>
        // A composited mock-up always has its design, template and URL; only an uploaded one may lack them.
        new(mockupImage.Id, mockupImage.ProductId, mockupImage.DesignImageId!.Value, mockupImage.MockupTemplateId!.Value,
            mockupImage.MockupImageUrl!, mockupImage.MockupWidthPx, mockupImage.MockupHeightPx, mockupImage.ApprovalStatus,
            mockupImage.GarmentColor, mockupImage.BackgroundColor);

    // Whether the template's garment can be lifted off its photo. A template analyzed before cut masks
    // were made gets its cut mask here, from the garment mask it already has.
    private async Task<bool> TryEnsureCutMaskAsync(MockupTemplate template, CancellationToken cancellationToken)
    {
        if (!MockupRules.HasCurrentMaps(template))
            return false;

        try
        {
            return await mapService.EnsureCutMaskAsync(
                compositor.BuildAssetUrl(template.BaseImageUrl, MockupRules.GarmentMaskKey(template)),
                compositor.BuildAssetUrl(template.BaseImageUrl, MockupRules.CutMaskKey(template)),
                MockupRules.CutMaskKey(template), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not prepare the cut mask of mock-up template {TemplateId}.", template.Id);
            return false;
        }
    }

    private async Task<bool> IsNameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var candidate = name.Trim().ToLowerInvariant();
        return await templates.Query()
            .AnyAsync(template => template.Name.ToLower() == candidate && template.Id != excludeId, cancellationToken);
    }

    // A side is never listed, but every template name is unique in the database, deleted ones included. So a side
    // keeps the asked name when it is free and otherwise gets a short part of its id added.
    private async Task<string> SideNameAsync(string requested, Guid sideId, CancellationToken cancellationToken)
    {
        var name = requested.Trim();
        if (!await IsNameTakenAsync(name, sideId, cancellationToken))
            return name;
        var suffix = $" ({sideId.ToString("N")[..8]})";
        return name[..Math.Min(name.Length, MockupRules.MaximumNameLength - suffix.Length)].TrimEnd() + suffix;
    }

    private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    // Preparing only adds realism; a photo that cannot be analyzed is still a usable base photo.
    private async Task<PreparedBasePhoto?> TryPrepareAsync(byte[] photo, CancellationToken cancellationToken, bool preview = false)
    {
        try
        {
            return await mapService.PrepareAsync(photo, preview, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not analyze a mock-up base photo.");
            return null;
        }
    }

    // For a photo in a format that is not analyzed as uploaded: its JPEG from the CDN is.
    private async Task<PreparedBasePhoto?> TryPrepareStoredAsync(string baseImageUrl, CancellationToken cancellationToken) =>
        await TryDownloadAsync(baseImageUrl, cancellationToken) is { } jpeg ? await TryPrepareAsync(jpeg, cancellationToken) : null;

    private async Task<byte[]?> TryDownloadAsync(string baseImageUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await mapService.DownloadAsync(baseImageUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not download mock-up base photo {Url}.", baseImageUrl);
            return null;
        }
    }

    // The upload's stream was already read for analysis, so the photo is sent from its bytes.
    private async Task<PublicImageUploadResult> UploadPhotoAsync(UploadFileDto upload, byte[] photo, string storageKey, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(photo);
        return await images.UploadImageWithMetadataAsync(new UploadFileDto(upload.FileName, upload.ContentType, stream.Length, stream), storageKey, cancellationToken);
    }

    /// <summary>
    /// Stores the helper images prepared from the template's current photo and records what they
    /// allow. A failure only turns the realism off; the returned error is set when recoloring was
    /// requested but the photo is unsuitable.
    /// </summary>
    private async Task<Error?> ApplyPreparedAsync(MockupTemplate template, PreparedBasePhoto? prepared, bool allowRecolor, CancellationToken cancellationToken)
    {
        template.PrintMapsSourceUrl = null;
        template.PrintMapsVersion = null;
        template.AllowRecolor = false;
        template.GarmentIsLight = false;
        if (prepared is null)
            return allowRecolor ? MockupErrors.NotRecolorable("the photo could not be analyzed.") : null;

        var version = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        try
        {
            await mapService.StoreHelpersAsync(MockupRules.HelperKeyPrefix(template.Id, version), prepared, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not store print maps for mock-up template {TemplateId}.", template.Id);
            return allowRecolor ? MockupErrors.NotRecolorable("the photo could not be analyzed.") : null;
        }

        template.PrintMapsSourceUrl = template.BaseImageUrl;
        template.PrintMapsVersion = version;

        var problem = MockupRules.RecolorProblem(prepared.Stats);
        template.GarmentIsLight = problem is null;
        if (!allowRecolor)
            return null;
        if (problem is not null)
            return MockupErrors.NotRecolorable(problem);

        template.AllowRecolor = true;
        return null;
    }

    public async Task<Result<GarmentMaskPreviewResponseDto>> PreviewGarmentMaskAsync(UploadFileDto? photo, CancellationToken cancellationToken = default)
    {
        if (currentUser.TryGetUserId() is not Guid userId)
            return Result.Failure<GarmentMaskPreviewResponseDto>(MockupErrors.Unauthenticated("preview"));

        var planCheck = await entitlements.RequireFeatureAsync(userId, PlanFeatures.CustomMockupTemplates, cancellationToken);
        if (planCheck.IsFailure) return Result.Failure<GarmentMaskPreviewResponseDto>(planCheck.Error);
        if (!MockupImageValidators.IsSupportedImage(photo) || !MockupImageValidators.IsWithinSizeLimit(photo))
            return Result.Failure<GarmentMaskPreviewResponseDto>(MockupErrors.InvalidPreviewPhoto());
        if (!mapService.IsAvailable)
            return Result.Failure<GarmentMaskPreviewResponseDto>(MockupErrors.ProcessingUnavailable());

        var bytes = await ReadAllAsync(photo!.Content, cancellationToken);
        // Nothing is stored for a preview, so a format that is only read from the CDN's JPEG has none to read.
        var prepared = MockupImageValidators.IsDirectlyReadable(bytes)
            ? await TryPrepareAsync(bytes, cancellationToken, preview: true)
            : null;
        if (prepared is null)
            return Result.Success(new GarmentMaskPreviewResponseDto(false, "the photo could not be analyzed.", null));

        var problem = MockupRules.RecolorProblem(prepared.Stats);
        return Result.Success(new GarmentMaskPreviewResponseDto(
            problem is null, problem,
            $"data:image/png;base64,{Convert.ToBase64String(prepared.MaskPng)}"));
    }

    private static Error? ApplyGarmentColor(MockupTemplate template, string? garmentColor)
    {
        if (garmentColor is not null && !template.AllowRecolor)
            return MockupErrors.RecolorNotAllowed(template.Name);

        template.GarmentColor = template.AllowRecolor ? garmentColor?.ToUpperInvariant() : null;
        return null;
    }

    private async Task TryDeleteImageAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await images.DeleteImageAsync(storageKey, cancellationToken);
        }
        catch (Exception)
        {
            // Best effort: the database write already failed, so the original exception must reach the caller unchanged.
        }
    }

    private static bool FitsWithin(MockupPosition position, int outputWidth, int outputHeight) =>
        position.X >= 0 && position.Y >= 0 && position.Width > 0 && position.Height > 0
        && position.X + position.Width <= outputWidth && position.Y + position.Height <= outputHeight;

    private static bool AreasFit(IEnumerable<MockupAreaDto> areas, int outputWidth, int outputHeight) =>
        areas.All(area => area.X >= 0 && area.Y >= 0 && MockupRules.AreaFits(area, outputWidth, outputHeight));

    /// <summary>
    /// A template's print areas as asked for, the main one first. Without areas, the one rectangle given is the main
    /// area, and a template being changed keeps its other areas and what its main area is called.
    /// </summary>
    private static IReadOnlyList<MockupAreaDto> RequestedAreas(IReadOnlyList<MockupAreaDto>? requested, MockupPosition main,
        MockupRules.MockupPrintLayout? current)
    {
        if (requested is { Count: > 0 })
        {
            return requested
                .OrderBy(area => area.Key == MockupRules.MainAreaKey ? 0 : 1)
                .Select(area => area with
                {
                    Name = string.IsNullOrWhiteSpace(area.Name) ? null : area.Name.Trim(),
                    Rotation = MockupRules.NormalizeRotation(area.Rotation),
                    Source = area.Source ?? (area.Key == MockupRules.MainAreaKey ? MockupRules.SourceSame : MockupRules.SourceNone),
                    DesignImageId = null,
                })
                .ToList();
        }

        var mainArea = current?.Main is { } kept
            ? kept with { X = main.X, Y = main.Y, Width = main.Width, Height = main.Height }
            : new MockupAreaDto(MockupRules.MainAreaKey, null, main.X, main.Y, main.Width, main.Height, Source: MockupRules.SourceSame);
        return [mainArea, .. current?.Areas.Skip(1) ?? []];
    }

    /// <summary>
    /// The designs laid on a mock-up, one per print area that prints one: the mock-up's own design, or another one
    /// found among <paramref name="others"/>. A chosen design that is gone is reported and its area left empty.
    /// </summary>
    private static List<MockupPlacement> Placements(IEnumerable<MockupRules.ResolvedArea> areas, DesignImage design,
        IReadOnlyDictionary<Guid, DesignImage> others, Action? missing = null)
    {
        var placements = new List<MockupPlacement>();
        foreach (var area in areas)
        {
            var printed = area.Source switch
            {
                MockupRules.SourceSame => design,
                MockupRules.SourceDesign when area.DesignImageId is Guid id && others.TryGetValue(id, out var other) => other,
                _ => null,
            };
            if (printed is null)
            {
                if (area.Source == MockupRules.SourceDesign)
                    missing?.Invoke();
                continue;
            }
            placements.Add(new MockupPlacement(printed.StorageKey, area.Position, printed.ImageWidthPx, printed.ImageHeightPx, area.Area.Rotation));
        }
        return placements;
    }

    // A mock-up of the design alone in one straight print area is made exactly as before there were several areas,
    // so its URL, and the mock-up already made with it, stay the same.
    private string Compose(string baseImageUrl, DesignImage design, IReadOnlyList<MockupPlacement> placements, MockupLayers layers) =>
        placements is [{ Rotation: 0, Corners: null } only] && only.OverlayStorageKey == design.StorageKey
            ? compositor.BuildCompositeUrl(baseImageUrl, design.StorageKey, only.Area, layers)
            : compositor.BuildCompositeUrl(baseImageUrl, placements, layers);

    private static string StorageKey(Guid templateId) => MockupRules.BaseImageKey(templateId);

    private MockupTemplateResponseDto Map(MockupTemplate template, Guid userId, IReadOnlyList<MockupTemplate>? sides = null)
    {
        var mapsReady = MockupRules.HasCurrentMaps(template);
        var recolor = mapsReady && template.AllowRecolor;
        return new(template.Id, template.Name, template.ProductType, template.BaseImageUrl, template.PreviewImageUrl,
            template.PrintAreaConfig, template.OutputWidthPx, template.OutputHeightPx, template.UsageCount ?? 0,
            template.IsSystemTemplate == true, template.UserId == userId, mapsReady, recolor,
            mapsReady ? compositor.BuildAssetUrl(template.BaseImageUrl, MockupRules.GarmentMaskKey(template)) : null,
            recolor ? template.GarmentColor : null,
            AllowBackground: mapsReady,
            Views: sides is { Count: > 0 } ? sides.Select(side => Map(side, userId)).ToList() : null);
    }

    private async Task<BatchJob?> FindOwnedBatchAsync(Guid batchJobId, CancellationToken cancellationToken)
    {
        var userId = currentUser.TryGetUserId();
        if (userId is null)
            return null;

        return await batches.Query()
            .Where(batch => batch.Id == batchJobId && batch.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<HashSet<string>> BatchProductTypesAsync(Guid batchJobId, CancellationToken cancellationToken)
    {
        var productIds = await rows.Query()
            .Where(row => row.BatchJobId == batchJobId && row.ProductId != null)
            .Select(row => row.ProductId!.Value)
            .ToListAsync(cancellationToken);

        if (productIds.Count == 0)
            return [];

        return (await products.Query()
                .Where(product => productIds.Contains(product.Id))
                .Select(product => product.ProductType.ToLower())
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<Guid> ReadSelection(string config)
    {
        try
        {
            var node = JsonNode.Parse(config);
            var ids = node?[MockupRules.ConfigKey]?.AsArray();
            if (ids is null)
                return [];

            return ids
                .Select(id => Guid.TryParse(id?.ToString(), out var value) ? value : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ReadColors(string config)
    {
        try
        {
            return JsonNode.Parse(config)?[MockupRules.GarmentColorsConfigKey]?.AsArray()
                .Select(color => color?.ToString())
                .Where(color => color is not null && System.Text.RegularExpressions.Regex.IsMatch(color, MockupRules.GarmentColorPattern))
                .Select(color => color!.ToUpperInvariant())
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Kept next to the selection in the job's config; written by ApplyAsync, read when mock-ups are made.
    private const string TemplateColorsConfigKey = "mockupTemplateColors";

    /// <summary>The job's approval mode, or null for a job started before approval existed.</summary>
    private static bool? ReadRequireApproval(string config)
    {
        try
        {
            return (bool?)JsonNode.Parse(config)?[GenerationConfigKeys.RequireApproval];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<Guid, IReadOnlyList<string>> ReadTemplateColors(string config)
    {
        var result = new Dictionary<Guid, IReadOnlyList<string>>();
        try
        {
            if (JsonNode.Parse(config)?[TemplateColorsConfigKey] is not JsonObject byTemplate)
                return result;

            foreach (var (key, value) in byTemplate)
            {
                if (!Guid.TryParse(key, out var templateId) || value is not JsonArray array)
                    continue;

                var colors = array
                    .Select(color => color?.ToString())
                    .Where(color => color is not null && System.Text.RegularExpressions.Regex.IsMatch(color, MockupRules.GarmentColorPattern))
                    .Select(color => color!.ToUpperInvariant())
                    .ToList();
                if (colors.Count > 0)
                    result[templateId] = colors;
            }
        }
        catch (JsonException)
        {
            result.Clear();
        }

        return result;
    }

    private static IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>> ReadTemplateBackgrounds(string config)
    {
        var result = new Dictionary<Guid, IReadOnlyDictionary<string, string>>();
        static bool IsColor(string? value) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, MockupRules.GarmentColorPattern);
        try
        {
            if (JsonNode.Parse(config)?[MockupRules.TemplateBackgroundsConfigKey] is not JsonObject byTemplate)
                return result;

            foreach (var (key, value) in byTemplate)
            {
                if (!Guid.TryParse(key, out var templateId))
                    continue;

                var byColor = new Dictionary<string, string>();
                if (value is JsonObject colors)
                {
                    foreach (var (garment, background) in colors)
                    {
                        var hex = background?.ToString();
                        if ((garment == MockupRules.DefaultBackgroundKey || IsColor(garment)) && IsColor(hex))
                            byColor[garment == MockupRules.DefaultBackgroundKey ? garment : garment.ToUpperInvariant()] = hex!.ToUpperInvariant();
                    }
                }
                else if (IsColor(value?.ToString()))
                {
                    // Saved before backgrounds went per garment color: one color for the whole template.
                    byColor[MockupRules.DefaultBackgroundKey] = value!.ToString().ToUpperInvariant();
                }

                if (byColor.Count > 0)
                    result[templateId] = byColor;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            result.Clear();
        }

        return result;
    }

    private static readonly JsonSerializerOptions OverrideJson = new(JsonSerializerDefaults.Web);

    private static IReadOnlyDictionary<string, MockupOverrideDto> ReadOverrides(string config) => MockupRules.ReadOverrides(config);

    private static string WriteOverrides(string config, IReadOnlyDictionary<string, MockupOverrideDto> overrides)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(config)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        if (overrides.Count == 0)
            root.Remove(MockupRules.OverridesConfigKey);
        else
            root[MockupRules.OverridesConfigKey] = JsonSerializer.SerializeToNode(overrides, OverrideJson);
        return root.ToJsonString();
    }

    private static IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>> NormalizeTemplateBackgrounds(IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>>? requested) =>
        (requested ?? new Dictionary<Guid, IReadOnlyDictionary<string, string>>())
            .Where(entry => entry.Value is { Count: > 0 })
            .ToDictionary(entry => entry.Key, entry => (IReadOnlyDictionary<string, string>)entry.Value.ToDictionary(
                color => color.Key == MockupRules.DefaultBackgroundKey ? color.Key : color.Key.ToUpperInvariant(),
                color => color.Value.ToUpperInvariant()));

    // Uppercased, and without templates that were given no color (they use the shared list).
    private static IReadOnlyDictionary<Guid, IReadOnlyList<string>> NormalizeTemplateColors(IReadOnlyDictionary<Guid, IReadOnlyList<string>>? requested) =>
        (requested ?? new Dictionary<Guid, IReadOnlyList<string>>())
            .Where(entry => entry.Value is { Count: > 0 })
            .ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value.Select(color => color.ToUpperInvariant()).ToList());

    private static string WriteSelection(
        string config,
        IReadOnlyList<Guid> templateIds,
        IReadOnlyList<string> garmentColors,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> colorsByTemplate,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>> backgrounds,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, MockupAreaSourceDto>> areaSources)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(config)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        var ids = new JsonArray();
        foreach (var id in templateIds)
        {
            ids.Add(id.ToString());
        }

        root[MockupRules.ConfigKey] = ids;
        root[MockupRules.GarmentColorsConfigKey] = new JsonArray(garmentColors.Select(color => (JsonNode?)JsonValue.Create(color)).ToArray());

        if (colorsByTemplate.Count == 0)
        {
            root.Remove(TemplateColorsConfigKey);
        }
        else
        {
            var byTemplate = new JsonObject();
            foreach (var (templateId, colors) in colorsByTemplate)
                byTemplate[templateId.ToString()] = new JsonArray(colors.Select(color => (JsonNode?)JsonValue.Create(color)).ToArray());
            root[TemplateColorsConfigKey] = byTemplate;
        }

        if (backgrounds.Count == 0)
        {
            root.Remove(MockupRules.TemplateBackgroundsConfigKey);
        }
        else
        {
            var byTemplate = new JsonObject();
            foreach (var (templateId, byColor) in backgrounds)
            {
                var colors = new JsonObject();
                foreach (var (garment, background) in byColor)
                    colors[garment] = background;
                byTemplate[templateId.ToString()] = colors;
            }
            root[MockupRules.TemplateBackgroundsConfigKey] = byTemplate;
        }

        MockupRules.WriteAreaSources(root, areaSources);
        return root.ToJsonString();
    }
}
