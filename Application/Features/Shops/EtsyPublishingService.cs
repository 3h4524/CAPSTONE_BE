using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Abstractions.Storage;
using APCS.Application.Common.Validation;
using APCS.Application.Features.ListingContent;
using APCS.Application.Features.Shops.Common;
using APCS.Application.Features.Shops.Dtos.Request;
using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.Shops;

/// <summary>
/// Pushes a product to Etsy as a draft listing. Each push is recorded in <c>etsy_upload_logs</c>; its
/// idempotency key is the product and a hash of what was sent, so the same push twice finds the first draft.
/// </summary>
public sealed class EtsyPublishingService(
    ICurrentUser currentUser,
    ShopService shops,
    IEtsyShopClient etsy,
    IListingContentService listingContent,
    IRepository<Product> products,
    IRepository<MockupImage> mockups,
    IRepository<DesignImage> designs,
    IRepository<PromoVideo> videos,
    IRepository<EtsyUploadLog> uploads,
    IRepository<EtsyIntegration> integrations,
    IMediaStorage storage,
    ICacheService cache,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IValidator<PublishEtsyDraftRequestDto> validator,
    ILogger<EtsyPublishingService> logger) : IEtsyPublishingService
{
    private const string TaxonomyCacheKey = "etsy:seller-taxonomy:v1";
    private static readonly TimeSpan TaxonomyLifetime = TimeSpan.FromDays(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Print-on-demand defaults for a first push: the seller designed it, a partner makes it to order.</summary>
    private static readonly EtsyListingSettingsDto FirstPushDefaults = new(null, 999, null, null, "physical", "i_did", "made_to_order", null, null, []);

    internal static string EditUrl(string listingId) => $"https://www.etsy.com/your/shops/me/listing-editor/edit/{listingId}";

    public async Task<Result<EtsyDraftResponseDto>> GetDraftAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<EtsyDraftResponseDto>(ShopErrors.Unauthenticated());

        var product = await OwnedProductAsync(productId, userId, cancellationToken);
        if (product is null)
            return Result.Failure<EtsyDraftResponseDto>(ShopErrors.ProductNotFound());

        var warnings = new List<string>();
        var content = await listingContent.GetAsync(productId, cancellationToken);
        if (content.IsFailure)
            warnings.Add("No listing content was generated for this product; write the title, description and tags here.");
        var (title, titleWarnings) = EtsyListingText.Title(content.IsSuccess ? content.Value.Title : product.Name);
        var (tags, tagWarnings) = EtsyListingText.Tags(content.IsSuccess ? content.Value.Tags.OrderBy(tag => tag.Position).Select(tag => tag.Value) : []);
        warnings.AddRange(titleWarnings);
        warnings.AddRange(tagWarnings);

        var approvedMockups = (await ApprovedMockupsAsync(productId, cancellationToken))
            .Select(mockup => new EtsyDraftMockupDto(mockup.Id, storage.SignRead(mockup.StorageKey, "image", mockup.StorageVersion), mockup.MockupWidthPx, mockup.MockupHeightPx))
            .ToArray();
        if (approvedMockups.Length == 0)
            warnings.Add("This product has no approved mock-up yet; approve at least one before pushing.");
        var video = await ApprovedVideoAsync(productId, cancellationToken);
        var approvedDesigns = (await ProductDesigns.ApprovedAsync(designs, mockups, productId, cancellationToken))
            .Select(ProductDesigns.ToDto)
            .ToArray();

        string status = ShopConnectionStatuses.NotConnected;
        string? shopName = null;
        string? currency = null;
        IReadOnlyList<EtsyOptionDto> shipping = [], processing = [], partners = [];
        EtsyIntegration? integration = null;
        try
        {
            if (await shops.ConnectedAccessAsync(userId, cancellationToken) is { } access)
            {
                integration = access.Integration;
                status = ShopConnectionStatuses.Connected;
                shopName = access.Integration.ShopName;
                var shopId = access.Integration.EtsyShopId;
                currency = (await etsy.GetShopAsync(access.AccessToken, shopId, cancellationToken)).CurrencyCode;
                shipping = Options(await etsy.GetShippingProfilesAsync(access.AccessToken, shopId, cancellationToken));
                processing = Options(await etsy.GetProcessingProfilesAsync(access.AccessToken, shopId, cancellationToken));
                partners = Options(await etsy.GetProductionPartnersAsync(access.AccessToken, shopId, cancellationToken));
            }
            else
            {
                status = await integrations.Query().AnyAsync(row => row.UserId == userId && row.DeletedAt == null && row.IsActive != false, cancellationToken)
                    ? ShopConnectionStatuses.ReconnectRequired
                    : ShopConnectionStatuses.NotConnected;
            }
        }
        catch (EtsyApiException exception) when (exception.Kind == EtsyFailureKind.Unauthorized)
        {
            if (integration is not null) await shops.MarkReconnectRequiredAsync(integration, cancellationToken);
            status = ShopConnectionStatuses.ReconnectRequired;
        }
        catch (EtsyApiException exception)
        {
            logger.LogWarning("Reading Etsy shop options for user {UserId} failed: {Kind}.", userId, exception.Kind);
            status = ShopConnectionStatuses.Unavailable;
        }
        if (status == ShopConnectionStatuses.Connected && shipping.Count == 0)
            warnings.Add("Your Etsy shop has no shipping profile; create one on Etsy before pushing a physical listing.");

        var history = await uploads.Query()
            .Where(row => row.ProductId == productId && row.EtsyIntegration.UserId == userId)
            .OrderByDescending(row => row.AttemptedAt)
            .Take(5)
            .Select(row => new { row.UploadStatus, row.EtsyListingId, row.AttemptedAt, row.ErrorMessage })
            .ToListAsync(cancellationToken);

        return Result.Success(new EtsyDraftResponseDto(
            product.Id, product.Name, status, shopName, currency,
            title, content.IsSuccess ? content.Value.Description : product.InputDescription, tags, warnings,
            approvedMockups,
            video is null ? null : new EtsyDraftVideoDto(video.Id, video.VideoDurationSeconds,
                video.ThumbnailStorageKey is null ? null : storage.SignRead(video.ThumbnailStorageKey, "image", video.ThumbnailStorageVersion)),
            await LastSettingsAsync(userId, cancellationToken),
            shipping, processing, partners,
            history.Select(row => new EtsyUploadHistoryDto(row.UploadStatus, row.EtsyListingId,
                row.EtsyListingId is null ? null : EditUrl(row.EtsyListingId),
                new DateTimeOffset(DateTime.SpecifyKind(row.AttemptedAt, DateTimeKind.Utc)), row.ErrorMessage)).ToArray(),
            approvedDesigns));
    }

    public async Task<Result<IReadOnlyList<EtsyTaxonomyOptionDto>>> SearchTaxonomyAsync(string? query, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure<IReadOnlyList<EtsyTaxonomyOptionDto>>(ShopErrors.Unauthenticated());

        var nodes = await cache.GetAsync<EtsyTaxonomyNode[]>(TaxonomyCacheKey, cancellationToken);
        if (nodes is null)
        {
            try
            {
                nodes = (await etsy.GetSellerTaxonomyAsync(cancellationToken)).ToArray();
            }
            catch (EtsyApiException exception)
            {
                logger.LogWarning("Reading the Etsy taxonomy failed: {Kind}.", exception.Kind);
                return Result.Failure<IReadOnlyList<EtsyTaxonomyOptionDto>>(ShopErrors.EtsyUnavailable());
            }
            await cache.SetAsync(TaxonomyCacheKey, nodes, TaxonomyLifetime, cancellationToken);
        }

        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = nodes
            .Where(node => words.All(word => node.Path.Contains(word, StringComparison.OrdinalIgnoreCase)))
            // The most specific category first: a leaf is what a listing belongs to.
            .OrderByDescending(node => words.Length > 0 && node.Path.EndsWith(words[^1], StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(node => node.Path.Count(character => character == '>'))
            .ThenBy(node => node.Path, StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .Select(node => new EtsyTaxonomyOptionDto(node.Id, node.Path))
            .ToArray();
        return Result.Success<IReadOnlyList<EtsyTaxonomyOptionDto>>(matches);
    }

    public async Task<Result<PublishEtsyDraftResponseDto>> PublishDraftAsync(Guid productId, PublishEtsyDraftRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<PublishEtsyDraftResponseDto>(ShopErrors.Unauthenticated());

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<PublishEtsyDraftResponseDto>(validation.ToValidationError());

        if (await OwnedProductAsync(productId, userId, cancellationToken) is null)
            return Result.Failure<PublishEtsyDraftResponseDto>(ShopErrors.ProductNotFound());

        // Only approved media of this product can be sent, in the order the seller chose.
        var approved = (await ApprovedMockupsAsync(productId, cancellationToken)).ToDictionary(mockup => mockup.Id);
        if (request.MockupIds.Any(id => !approved.ContainsKey(id)))
            return Result.Failure<PublishEtsyDraftResponseDto>(ShopErrors.MockupNotApproved());
        var video = request.IncludeVideo ? await ApprovedVideoAsync(productId, cancellationToken) : null;
        if (request.IncludeVideo && video is null)
            return Result.Failure<PublishEtsyDraftResponseDto>(ShopErrors.VideoNotApproved());
        var designIds = request.Type == "download" ? request.DesignIds ?? [] : [];
        var approvedDesigns = designIds.Count == 0
            ? new Dictionary<Guid, DesignImage>()
            : (await ProductDesigns.ApprovedAsync(designs, mockups, productId, cancellationToken)).ToDictionary(design => design.Id);
        if (designIds.Any(id => !approvedDesigns.ContainsKey(id)))
            return Result.Failure<PublishEtsyDraftResponseDto>(ShopErrors.DesignNotApproved());

        var (title, _) = EtsyListingText.Title(request.Title);
        var (tags, _) = EtsyListingText.Tags(request.Tags);
        var draft = new EtsyDraftListing(title, request.Description.Trim(), request.Price, request.Quantity, request.TaxonomyId,
            request.Type, request.WhoMade, request.WhenMade, tags,
            request.Type == "physical" ? request.ShippingProfileId : null,
            request.Type == "physical" ? request.ReadinessStateId : null,
            request.ProductionPartnerIds);

        (EtsyIntegration Integration, string AccessToken)? access;
        try
        {
            access = await shops.ConnectedAccessAsync(userId, cancellationToken);
        }
        catch (EtsyApiException exception) when (exception.Kind == EtsyFailureKind.Unauthorized)
        {
            access = null;
        }
        if (access is not { } connection)
            return Result.Failure<PublishEtsyDraftResponseDto>(ShopErrors.ReconnectRequired());

        var payload = JsonSerializer.Serialize(new UploadPayload(draft, request.TaxonomyPath, request.MockupIds, video?.Id, designIds), Json);
        var key = $"product:{productId}:{Hash(payload)}";
        var log = (await uploads.FindAsync(row => row.EtsyIntegrationId == connection.Integration.Id && row.IdempotencyKey == key, cancellationToken)).SingleOrDefault();
        if (log is { UploadStatus: "success", EtsyListingId: { } existing })
            return Result.Success(new PublishEtsyDraftResponseDto(existing, EditUrl(existing), 0, false, true, []));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (log is null)
        {
            log = new EtsyUploadLog
            {
                Id = Guid.NewGuid(), EtsyIntegrationId = connection.Integration.Id, ProductId = productId, IdempotencyKey = key,
                UploadStatus = "pending", ListingState = "draft", PublishImmediately = false, UploadPayload = payload,
                RetryCount = 0, AttemptedAt = now, CreatedAt = now,
            };
            await uploads.AddAsync(log, saveChange: true, cancellationToken);
        }
        else
        {
            // A failed push of the same content is tried again under the same record.
            log.UploadStatus = "retrying";
            log.RetryCount = (log.RetryCount ?? 0) + 1;
            log.AttemptedAt = now;
            log.ErrorMessage = null;
            await uploads.UpdateAsync(log, saveChange: true, cancellationToken);
        }

        EtsyCreatedListing listing;
        try
        {
            listing = await etsy.CreateDraftListingAsync(connection.AccessToken, connection.Integration.EtsyShopId, draft, cancellationToken);
        }
        catch (EtsyApiException exception)
        {
            await FailAsync(log, exception.Kind == EtsyFailureKind.RateLimited ? "Etsy is limiting requests; try again in a minute." : $"Etsy refused the listing: {exception.Reason ?? exception.Message}", cancellationToken);
            if (exception.Kind == EtsyFailureKind.Unauthorized)
            {
                await shops.MarkReconnectRequiredAsync(connection.Integration, cancellationToken);
                return Result.Failure<PublishEtsyDraftResponseDto>(ShopErrors.ReconnectRequired());
            }
            return Result.Failure<PublishEtsyDraftResponseDto>(ShopErrors.PublishFailed(exception.Kind, exception.Reason));
        }

        // The draft exists from here on: a failed image or video is reported, not a reason to make another draft.
        log.EtsyListingId = listing.ListingId;
        await uploads.UpdateAsync(log, saveChange: true, cancellationToken);
        var warnings = new List<string>();
        var imagesUploaded = 0;
        foreach (var (mockupId, index) in request.MockupIds.Select((id, index) => (id, index)))
        {
            var mockup = approved[mockupId];
            try
            {
                var image = await storage.ReadImageAsync(mockup.StorageKey, mockup.StorageVersion, cancellationToken);
                await etsy.UploadListingImageAsync(connection.AccessToken, connection.Integration.EtsyShopId, listing.ListingId, image,
                    $"mockup-{index + 1}.png", index + 1, title, cancellationToken);
                imagesUploaded++;
            }
            catch (Exception exception) when (exception is EtsyApiException or HttpRequestException or IOException)
            {
                logger.LogWarning("Image {Index} of Etsy listing {ListingId} failed: {Error}.", index + 1, listing.ListingId, exception.GetType().Name);
                warnings.Add($"Image {index + 1} could not be uploaded; add it on Etsy.");
            }
        }

        var videoUploaded = false;
        if (video is not null)
        {
            try
            {
                await etsy.UploadListingVideoAsync(connection.AccessToken, connection.Integration.EtsyShopId, listing.ListingId,
                    storage.SignRead(video.StorageKey!, "video", video.StorageVersion), title, cancellationToken);
                videoUploaded = true;
            }
            catch (EtsyApiException exception)
            {
                logger.LogWarning("Video of Etsy listing {ListingId} failed: {Kind}.", listing.ListingId, exception.Kind);
                warnings.Add("The video could not be uploaded; add it on Etsy.");
            }
        }

        var filesUploaded = 0;
        foreach (var (designId, index) in designIds.Select((id, index) => (id, index)))
        {
            var design = approvedDesigns[designId];
            if (design.FileSizeMb > MaxFileSizeMb)
            {
                warnings.Add($"Design file {index + 1} is larger than Etsy's {MaxFileSizeMb} MB limit; attach a smaller copy on Etsy.");
                continue;
            }
            try
            {
                await etsy.UploadListingFileAsync(connection.AccessToken, connection.Integration.EtsyShopId, listing.ListingId, design.ImageUrl,
                    ProductDesigns.FileName(design, index + 1), index + 1, cancellationToken);
                filesUploaded++;
            }
            catch (Exception exception) when (exception is EtsyApiException or HttpRequestException or IOException)
            {
                logger.LogWarning("File {Index} of Etsy listing {ListingId} failed: {Error}.", index + 1, listing.ListingId, exception.GetType().Name);
                warnings.Add($"Design file {index + 1} could not be attached; add it on Etsy before publishing, or buyers get nothing to download.");
            }
        }

        log.UploadStatus = "success";
        log.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        log.ErrorMessage = warnings.Count == 0 ? null : string.Join(" ", warnings);
        log.ApiResponse = JsonSerializer.Serialize(new { listingId = listing.ListingId, imagesUploaded, videoUploaded, filesUploaded }, Json);
        await uploads.UpdateAsync(log, cancellationToken: cancellationToken);
        connection.Integration.TotalListingsCreated = (connection.Integration.TotalListingsCreated ?? 0) + 1;
        connection.Integration.UpdatedAt = log.CompletedAt;
        await integrations.UpdateAsync(connection.Integration, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PublishEtsyDraftResponseDto(listing.ListingId, EditUrl(listing.ListingId), imagesUploaded, videoUploaded, false, warnings, filesUploaded));
    }

    private async Task FailAsync(EtsyUploadLog log, string message, CancellationToken cancellationToken)
    {
        log.UploadStatus = "failed";
        log.ErrorMessage = message.Length <= 2000 ? message : message[..2000];
        log.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        await uploads.UpdateAsync(log, saveChange: true, cancellationToken);
    }

    private Task<Product?> OwnedProductAsync(Guid productId, Guid userId, CancellationToken cancellationToken) =>
        products.Query().Where(product => product.Id == productId && product.UserId == userId && product.DeletedAt == null)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Mock-ups approved at their current revision, as the workflow counts them.</summary>
    private async Task<IReadOnlyList<MockupImage>> ApprovedMockupsAsync(Guid productId, CancellationToken cancellationToken) =>
        await mockups.Query()
            .Where(mockup => mockup.ProductId == productId && mockup.DeletedAt == null && mockup.ApprovalStatus == "approved"
                && mockup.ApprovedRevision == mockup.MetadataRevision)
            .OrderBy(mockup => mockup.CreatedAt)
            .Take(Validators.PublishEtsyDraftValidator.MaxImages)
            .ToListAsync(cancellationToken);

    /// <summary>The newest approved, stored promo video of the product.</summary>
    private Task<PromoVideo?> ApprovedVideoAsync(Guid productId, CancellationToken cancellationToken) =>
        videos.Query()
            .Where(video => video.ProductId == productId && video.DeletedAt == null && video.ApprovalStatus == "approved" && video.StorageKey != null)
            .OrderByDescending(video => video.UpdatedAt ?? video.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The settings of the seller's last push, so the form starts where they left off.</summary>
    private async Task<EtsyListingSettingsDto> LastSettingsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var last = await uploads.Query()
            .Where(row => row.EtsyIntegration.UserId == userId)
            .OrderByDescending(row => row.AttemptedAt)
            .Select(row => row.UploadPayload)
            .FirstOrDefaultAsync(cancellationToken);
        if (last is null)
            return FirstPushDefaults;
        try
        {
            var payload = JsonSerializer.Deserialize<UploadPayload>(last, Json);
            if (payload?.Listing is not { } listing)
                return FirstPushDefaults;
            return new EtsyListingSettingsDto(listing.Price, listing.Quantity, listing.TaxonomyId, payload.TaxonomyPath, listing.Type,
                listing.WhoMade, listing.WhenMade, listing.ShippingProfileId, listing.ReadinessStateId, listing.ProductionPartnerIds);
        }
        catch (JsonException)
        {
            return FirstPushDefaults;
        }
    }

    private static IReadOnlyList<EtsyOptionDto> Options(IReadOnlyList<EtsyOption> options) =>
        options.Select(option => new EtsyOptionDto(option.Id, option.Label)).ToArray();

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32].ToLowerInvariant();

    /// <summary>What a push sent, stored on its log row; also the source of the next push's defaults.</summary>
    internal sealed record UploadPayload(EtsyDraftListing Listing, string? TaxonomyPath, IReadOnlyList<Guid> MockupIds, Guid? VideoId, IReadOnlyList<Guid>? DesignIds = null);

    /// <summary>Etsy's size limit for one file of a digital listing.</summary>
    private const decimal MaxFileSizeMb = 20;
}
