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
/// Printify as the print-on-demand partner of the seller's Etsy shop. The seller's Printify key is managed on the
/// API Keys page (<c>api_keys</c>, provider <c>printify</c>); this service only reads it. The store it publishes
/// through is recorded on <c>printify_integrations</c> (without the key), and each product created is one row of
/// <c>printify_upload_logs</c>, whose <c>api_response</c> follows it from created to published on Etsy.
/// </summary>
public sealed class PrintifyService(
    ICurrentUser currentUser,
    IPrintifyClient printify,
    IApiKeyRepository keys,
    IApiKeyCredentials credentials,
    ShopService shops,
    IEtsyShopClient etsy,
    IListingContentService listingContent,
    IRepository<Product> products,
    IRepository<DesignImage> designs,
    IRepository<MockupImage> mockups,
    IRepository<PromoVideo> videos,
    IRepository<PrintifyIntegration> integrations,
    IRepository<PrintifyUploadLog> uploads,
    IMediaStorage storage,
    ICacheService cache,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IValidator<CreatePrintifyProductRequestDto> createValidator,
    IValidator<PublishPrintifyProductRequestDto> publishValidator,
    ILogger<PrintifyService> logger) : IPrintifyService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CatalogLifetime = TimeSpan.FromDays(1);

    /// <summary>How long the store found for a key is reused before Printify is asked again.</summary>
    private static readonly TimeSpan StoreLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The provider name the API Keys feature stores a Printify key under.</summary>
    internal const string KeyProvider = "printify";

    /// <summary>How long after asking Printify to publish an unlocked product without an Etsy listing still counts as publishing.</summary>
    internal static readonly TimeSpan PublishGrace = TimeSpan.FromMinutes(3);

    /// <summary>APCS images added in front of Printify's own on the Etsy listing (Etsy shows up to 20 in all).</summary>
    internal const int MaxAttachedImages = 10;

    internal const decimal DefaultPrice = 24.99m;

    internal static string EtsyListingUrl(string listingId) => $"https://www.etsy.com/listing/{listingId}";

    internal static string StoreKey(Guid userId, Guid keyId) => $"printify:store:{userId}:{keyId}";

    public async Task<Result<PrintifyConnectionDto>> GetConnectionAsync(bool refresh, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<PrintifyConnectionDto>(ShopErrors.Unauthenticated());
        return Result.Success((await ResolveAsync(userId, refresh, cancellationToken)).Connection);
    }

    /// <summary>
    /// The seller's Printify key from API Keys and the store it publishes through. A key counts only while API Keys
    /// shows it as connected (active, last check passed, not expired); the store is the one linked to Etsy.
    /// </summary>
    private async Task<(PrintifyConnectionDto Connection, PrintifyAccess? Access)> ResolveAsync(Guid userId, bool refresh, CancellationToken cancellationToken)
    {
        var key = (await keys.ListOwnedAsync(userId, cancellationToken))
            .Where(row => string.Equals(row.ServiceProvider.Trim(), KeyProvider, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.CreatedAt)
            .FirstOrDefault();
        if (key is null)
            return (Status(PrintifyStatuses.NotConnected, null, null), null);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (key.IsActive != true || key.LastCheckSucceeded != true || key.ExpiresAt <= now)
            return (Status(PrintifyStatuses.KeyInvalid, key, null), null);

        string token;
        try
        {
            token = credentials.Unprotect(key.KeyValueEncrypted).Trim();
        }
        catch (CryptographicException)
        {
            // Saved under a key ring this server no longer has: the seller replaces it on API Keys.
            return (Status(PrintifyStatuses.KeyInvalid, key, null), null);
        }

        var storeKey = StoreKey(userId, key.Id);
        var store = refresh ? null : await cache.GetAsync<PrintifyShop>(storeKey, cancellationToken);
        if (store is null)
        {
            try
            {
                store = PickStore(await printify.GetShopsAsync(token, cancellationToken));
            }
            catch (PrintifyApiException exception) when (exception.Kind == PrintifyFailureKind.Unauthorized)
            {
                logger.LogWarning("Printify refused the API key of user {UserId}.", userId);
                return (Status(PrintifyStatuses.KeyInvalid, key, null), null);
            }
            catch (PrintifyApiException exception)
            {
                logger.LogWarning("Reading Printify stores for user {UserId} failed: {Kind}.", userId, exception.Kind);
                return (Status(PrintifyStatuses.Unavailable, key, null), null);
            }
            if (store is null)
                return (Status(PrintifyStatuses.NoStore, key, null), null);
            await cache.SetAsync(storeKey, store, StoreLifetime, cancellationToken);
        }

        var integration = await RecordStoreAsync(userId, store, cancellationToken);
        return (Status(PrintifyStatuses.Connected, key, integration), new PrintifyAccess(integration, token, store.Id, key.Id));
    }

    /// <summary>The store that publishes to Etsy (it is the one Printify fulfils Etsy orders from), else the only one.</summary>
    internal static PrintifyShop? PickStore(IReadOnlyList<PrintifyShop> stores) =>
        stores.Where(store => store.SalesChannel == "etsy").OrderBy(store => store.Id).FirstOrDefault()
        ?? stores.OrderBy(store => store.Id).FirstOrDefault();

    /// <summary>
    /// Keeps the seller's <c>printify_integrations</c> row on the store in use: upload logs hang off it. The key is
    /// not copied there; it stays on <c>api_keys</c>.
    /// </summary>
    private async Task<PrintifyIntegration> RecordStoreAsync(Guid userId, PrintifyShop store, CancellationToken cancellationToken)
    {
        var storeId = store.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rows = await integrations.FindAsync(row => row.UserId == userId && row.DeletedAt == null, cancellationToken);
        var integration = rows.FirstOrDefault(row => row.PrintifyStoreId == storeId) ?? rows.OrderByDescending(row => row.UpdatedAt ?? row.CreatedAt).FirstOrDefault();
        var name = Truncate(store.Title.Length > 0 ? store.Title : $"Store {store.Id}");
        if (integration is not null && integration.PrintifyStoreId == storeId && integration.ShopName == name
            && integration.ShopTitle == store.SalesChannel && integration.IsActive == true && integration.PrintifyApiTokenEncrypted.Length == 0)
            return integration;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var added = integration is null;
        integration ??= new PrintifyIntegration { Id = Guid.NewGuid(), UserId = userId, TotalProductsUploaded = 0, CreatedAt = now };
        integration.PrintifyStoreId = storeId;
        integration.PrintifyApiTokenEncrypted = string.Empty;
        integration.ShopName = name;
        integration.ShopTitle = store.SalesChannel;
        integration.IsActive = true;
        integration.IsDefault = true;
        integration.LastSyncAt = now;
        integration.UpdatedAt = now;
        if (added)
            await integrations.AddAsync(integration, saveChange: false, cancellationToken);
        else
            await integrations.UpdateAsync(integration, saveChange: false, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return integration;
    }

    private static PrintifyConnectionDto Status(string status, ApiKey? key, PrintifyIntegration? integration) =>
        new(status,
            integration is not null && long.TryParse(integration.PrintifyStoreId, out var id) ? id : null,
            integration?.ShopName,
            integration?.ShopTitle,
            key?.KeyIdentifier,
            key?.LastCheckedAt is { } checkedAt ? new DateTimeOffset(DateTime.SpecifyKind(checkedAt, DateTimeKind.Utc)) : null,
            integration?.TotalProductsUploaded ?? 0);

    public async Task<Result<IReadOnlyList<PrintifyBlueprintDto>>> SearchBlueprintsAsync(string? query, CancellationToken cancellationToken = default)
    {
        var access = await AccessAsync(cancellationToken);
        if (access.IsFailure)
            return Result.Failure<IReadOnlyList<PrintifyBlueprintDto>>(access.Error);
        try
        {
            var blueprints = await CachedAsync("printify:blueprints:v1", () => printify.GetBlueprintsAsync(access.Value.Token, cancellationToken), cancellationToken);
            return Result.Success(RankBlueprints(blueprints, query));
        }
        catch (PrintifyApiException exception)
        {
            return Result.Failure<IReadOnlyList<PrintifyBlueprintDto>>(await FailureAsync(access.Value, exception, cancellationToken));
        }
    }

    public async Task<Result<IReadOnlyList<PrintifyProviderDto>>> GetProvidersAsync(long blueprintId, CancellationToken cancellationToken = default)
    {
        var access = await AccessAsync(cancellationToken);
        if (access.IsFailure)
            return Result.Failure<IReadOnlyList<PrintifyProviderDto>>(access.Error);
        try
        {
            var token = access.Value.Token;
            var makers = await CachedAsync($"printify:blueprint:{blueprintId}:providers:v1", () => printify.GetBlueprintProvidersAsync(token, blueprintId, cancellationToken), cancellationToken);
            var locations = (await CachedAsync("printify:providers:v1", () => printify.GetProvidersAsync(token, cancellationToken), cancellationToken))
                .ToDictionary(provider => provider.Id);
            var providers = makers
                .Select(maker => locations.TryGetValue(maker.Id, out var known) ? known with { Title = maker.Title.Length > 0 ? maker.Title : known.Title } : maker)
                // Buyers are mostly in the United States: a provider there ships fastest and cheapest.
                .OrderByDescending(provider => IsUnitedStates(provider.Country))
                .ThenBy(provider => provider.Title, StringComparer.OrdinalIgnoreCase)
                .Select(provider => new PrintifyProviderDto(provider.Id, provider.Title, provider.Country, provider.Region))
                .ToArray();
            return Result.Success<IReadOnlyList<PrintifyProviderDto>>(providers);
        }
        catch (PrintifyApiException exception)
        {
            return Result.Failure<IReadOnlyList<PrintifyProviderDto>>(await FailureAsync(access.Value, exception, cancellationToken));
        }
    }

    public async Task<Result<PrintifyCatalogDto>> GetCatalogAsync(long blueprintId, long providerId, CancellationToken cancellationToken = default)
    {
        var access = await AccessAsync(cancellationToken);
        if (access.IsFailure)
            return Result.Failure<PrintifyCatalogDto>(access.Error);
        try
        {
            var (variants, shipping) = await CatalogAsync(access.Value.Token, blueprintId, providerId, cancellationToken);
            var us = UsShipping(shipping, variants.Select(variant => variant.Id));
            return Result.Success(new PrintifyCatalogDto(
                blueprintId, providerId,
                variants.Select(variant => new PrintifyVariantOptionDto(variant.Id, variant.Title, variant.Color, variant.Size)).ToArray(),
                variants.Select(variant => variant.Color).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                variants.Select(variant => variant.Size).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                variants.SelectMany(variant => variant.Placeholders).Select(area => area.Position).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                us?.First, us?.Additional));
        }
        catch (PrintifyApiException exception)
        {
            return Result.Failure<PrintifyCatalogDto>(await FailureAsync(access.Value, exception, cancellationToken));
        }
    }

    public async Task<Result<PrintifyDraftResponseDto>> GetDraftAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<PrintifyDraftResponseDto>(ShopErrors.Unauthenticated());
        var product = await OwnedProductAsync(productId, userId, cancellationToken);
        if (product is null)
            return Result.Failure<PrintifyDraftResponseDto>(ShopErrors.ProductNotFound());

        var warnings = new List<string>();
        var content = await listingContent.GetAsync(productId, cancellationToken);
        if (content.IsFailure)
            warnings.Add("No listing content was generated for this product; write the title, description and tags here.");
        var (title, titleWarnings) = EtsyListingText.Title(content.IsSuccess ? content.Value.Title : product.Name);
        var (tags, tagWarnings) = EtsyListingText.Tags(content.IsSuccess ? content.Value.Tags.OrderBy(tag => tag.Position).Select(tag => tag.Value) : []);
        warnings.AddRange(titleWarnings);
        warnings.AddRange(tagWarnings);

        var approvedDesigns = (await ProductDesigns.ApprovedAsync(designs, mockups, productId, cancellationToken)).Select(ProductDesigns.ToDto).ToArray();
        if (approvedDesigns.Length == 0)
            warnings.Add("This product has no approved design yet; approve one before sending it to Printify.");
        var approvedMockups = (await ApprovedMockupsAsync(productId, cancellationToken))
            .Select(mockup => new EtsyDraftMockupDto(mockup.Id, storage.SignRead(mockup.StorageKey, "image", mockup.StorageVersion), mockup.MockupWidthPx, mockup.MockupHeightPx))
            .ToArray();
        var video = await ApprovedVideoAsync(productId, cancellationToken);

        var connection = (await ResolveAsync(userId, refresh: false, cancellationToken)).Connection;
        if (connection.Status == PrintifyStatuses.Connected && connection.SalesChannel != "etsy")
            warnings.Add("The connected Printify store is not linked to Etsy, so products published from it will not reach your Etsy shop.");
        var etsyConnected = await EtsyAccessAsync(userId, cancellationToken) is not null;

        var rows = await uploads.Query()
            .Where(row => row.ProductId == productId && row.PrintifyIntegration.UserId == userId)
            .OrderByDescending(row => row.AttemptedAt)
            .Take(5)
            .Select(row => new { row.PrintifyProductId, row.UploadStatus, row.ApiResponse, row.AttemptedAt, row.ErrorMessage })
            .ToListAsync(cancellationToken);
        var history = rows.Select(row =>
        {
            var state = LogState.Read(row.ApiResponse);
            return new PrintifyHistoryDto(row.PrintifyProductId, row.UploadStatus == "failed" ? "failed" : state.Stage, state.EtsyListingId,
                state.EtsyListingId is null ? null : EtsyListingUrl(state.EtsyListingId),
                new DateTimeOffset(DateTime.SpecifyKind(row.AttemptedAt, DateTimeKind.Utc)), row.ErrorMessage);
        }).ToArray();

        return Result.Success(new PrintifyDraftResponseDto(
            product.Id, product.Name, connection, title, content.IsSuccess ? content.Value.Description : product.InputDescription, tags,
            warnings, approvedDesigns, approvedMockups,
            video is null ? null : new EtsyDraftVideoDto(video.Id, video.VideoDurationSeconds,
                video.ThumbnailStorageKey is null ? null : storage.SignRead(video.ThumbnailStorageKey, "image", video.ThumbnailStorageVersion)),
            await LastSettingsAsync(userId, cancellationToken), history, etsyConnected));
    }

    public async Task<Result<PrintifyProductDto>> CreateProductAsync(Guid productId, CreatePrintifyProductRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<PrintifyProductDto>(ShopErrors.Unauthenticated());
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<PrintifyProductDto>(validation.ToValidationError());
        if (await OwnedProductAsync(productId, userId, cancellationToken) is null)
            return Result.Failure<PrintifyProductDto>(ShopErrors.ProductNotFound());
        var design = (await ProductDesigns.ApprovedAsync(designs, mockups, productId, cancellationToken)).FirstOrDefault(row => row.Id == request.DesignId);
        if (design is null)
            return Result.Failure<PrintifyProductDto>(ShopErrors.DesignNotApproved());

        var access = await AccessAsync(cancellationToken);
        if (access.IsFailure)
            return Result.Failure<PrintifyProductDto>(access.Error);
        var (integration, token, shopId, _) = access.Value;

        IReadOnlyList<PrintifyCatalogVariant> variants;
        IReadOnlyList<PrintifyShippingRate> shipping;
        try
        {
            (variants, shipping) = await CatalogAsync(token, request.BlueprintId, request.PrintProviderId, cancellationToken);
        }
        catch (PrintifyApiException exception)
        {
            return Result.Failure<PrintifyProductDto>(await FailureAsync(access.Value, exception, cancellationToken));
        }
        var chosen = request.VariantIds.ToHashSet();
        var selected = variants.Where(variant => chosen.Contains(variant.Id)).ToArray();
        if (selected.Length != chosen.Count)
            return Result.Failure<PrintifyProductDto>(Error.Validation("Some chosen colors or sizes are not made by this provider any more; choose them again."));
        var position = request.Position
            ?? (selected.All(variant => variant.Placeholders.Any(area => area.Position == "front")) ? "front" : selected[0].Placeholders.FirstOrDefault()?.Position);
        if (position is null || selected.Any(variant => variant.Placeholders.All(area => area.Position != position)))
            return Result.Failure<PrintifyProductDto>(Error.Validation("Not every chosen variant can be printed in that area; choose another print area or fewer variants."));

        var (title, _) = EtsyListingText.Title(request.Title);
        var (tags, _) = EtsyListingText.Tags(request.Tags);
        var payload = new CreatePayload(design.Id, request.BlueprintId, request.BlueprintTitle, request.PrintProviderId, request.PrintProviderTitle,
            request.VariantIds.Order().ToArray(),
            selected.Select(variant => variant.Color).OfType<string>().Distinct().ToArray(),
            selected.Select(variant => variant.Size).OfType<string>().Distinct().ToArray(),
            title, request.Description.Trim(), tags, request.Price, request.Scale, position);
        var payloadJson = JsonSerializer.Serialize(payload, Json);
        var key = $"product:{productId}:create:{Hash(payloadJson)}";

        var log = (await uploads.FindAsync(row => row.PrintifyIntegrationId == integration.Id && row.IdempotencyKey == key, cancellationToken)).SingleOrDefault();
        if (log is { UploadStatus: "success", PrintifyProductId: { } existingId })
        {
            // The same push again: show the product already made instead of making a second one.
            try
            {
                var existing = await printify.GetProductAsync(token, shopId, existingId, cancellationToken);
                return Result.Success(ToDto(productId, existing, LogState.Read(log.ApiResponse), shipping, alreadyCreated: true, []));
            }
            catch (PrintifyApiException exception) when (exception.Kind != PrintifyFailureKind.Rejected)
            {
                return Result.Failure<PrintifyProductDto>(await FailureAsync(access.Value, exception, cancellationToken));
            }
            catch (PrintifyApiException)
            {
                // Deleted on Printify since: make it again under the same record.
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (log is null)
        {
            log = new PrintifyUploadLog
            {
                Id = Guid.NewGuid(), PrintifyIntegrationId = integration.Id, ProductId = productId, IdempotencyKey = key, SyncType = "create",
                UploadPayload = payloadJson, UploadStatus = "pending", RetryCount = 0, AttemptedAt = now, CreatedAt = now,
            };
            await uploads.AddAsync(log, saveChange: true, cancellationToken);
        }
        else
        {
            log.UploadStatus = "retrying";
            log.RetryCount = (log.RetryCount ?? 0) + 1;
            log.AttemptedAt = now;
            log.ErrorMessage = null;
            log.PrintifyProductId = null;
            await uploads.UpdateAsync(log, saveChange: true, cancellationToken);
        }

        PrintifyProduct created;
        try
        {
            // Printify fetches the original design from its public URL: the recommended route for large files.
            var image = await printify.UploadImageAsync(token, ProductDesigns.FileName(design, 1), design.ImageUrl, cancellationToken);
            var priceCents = PrintifyPricing.ToCents(request.Price);
            created = await printify.CreateProductAsync(token, shopId, new PrintifyNewProduct(
                title, payload.Description, tags, request.BlueprintId, request.PrintProviderId,
                // Every variant the provider makes is sent; only the chosen ones are sold.
                variants.Select(variant => new PrintifyVariantPrice(variant.Id, priceCents, chosen.Contains(variant.Id))).ToArray(),
                image.Id, position, request.Scale), cancellationToken);
        }
        catch (PrintifyApiException exception)
        {
            await FailAsync(log, exception, cancellationToken);
            return Result.Failure<PrintifyProductDto>(await FailureAsync(access.Value, exception, cancellationToken));
        }

        var state = new LogState(PrintifyStages.Created, null, null, 0, false);
        log.PrintifyProductId = created.Id;
        log.UploadStatus = "success";
        log.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        log.ApiResponse = state.Write();
        await uploads.UpdateAsync(log, saveChange: false, cancellationToken);
        integration.TotalProductsUploaded = (integration.TotalProductsUploaded ?? 0) + 1;
        integration.LastSyncAt = log.CompletedAt;
        integration.UpdatedAt = log.CompletedAt;
        await integrations.UpdateAsync(integration, saveChange: false, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var warnings = new List<string>();
        if (design.ImageWidthPx < 3000)
            warnings.Add($"The design is {design.ImageWidthPx}px wide; Printify recommends about 4500px for a sharp print on a shirt front. Check the preview on Printify.");
        return Result.Success(ToDto(productId, created, state, shipping, alreadyCreated: false, warnings));
    }

    public async Task<Result<PrintifyProductDto>> PublishAsync(Guid productId, string printifyProductId, PublishPrintifyProductRequestDto request, CancellationToken cancellationToken = default)
    {
        var validation = await publishValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure<PrintifyProductDto>(validation.ToValidationError());
        var context = await ProductContextAsync(productId, printifyProductId, cancellationToken);
        if (context.IsFailure)
            return Result.Failure<PrintifyProductDto>(context.Error);
        var (access, log) = context.Value;
        var (integration, token, shopId, _) = access;

        try
        {
            var current = await printify.GetProductAsync(token, shopId, printifyProductId, cancellationToken);
            var prices = request.Prices.ToDictionary(price => price.VariantId, price => price.Price);
            var unknown = prices.Keys.Except(current.Variants.Select(variant => variant.Id)).Any();
            if (unknown)
                return Result.Failure<PrintifyProductDto>(Error.Validation("A priced variant is not part of this Printify product."));
            var belowCost = current.Variants.FirstOrDefault(variant => prices.TryGetValue(variant.Id, out var price) && PrintifyPricing.ToCents(price) < variant.CostCents);
            if (belowCost is not null)
                return Result.Failure<PrintifyProductDto>(Error.Validation($"{belowCost.Title} is priced below what Printify charges to make it (${PrintifyPricing.FromCents(belowCost.CostCents):0.00})."));

            // Variants left out of the prices are not sold; the ones priced are.
            await printify.UpdatePricesAsync(token, shopId, printifyProductId, current.Variants
                .Select(variant => prices.TryGetValue(variant.Id, out var price)
                    ? new PrintifyVariantPrice(variant.Id, PrintifyPricing.ToCents(price), true)
                    : new PrintifyVariantPrice(variant.Id, variant.PriceCents, false))
                .ToArray(), cancellationToken);
            await printify.PublishAsync(token, shopId, printifyProductId, cancellationToken);
        }
        catch (PrintifyApiException exception)
        {
            return Result.Failure<PrintifyProductDto>(await FailureAsync(access, exception, cancellationToken));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var state = LogState.Read(log.ApiResponse) with { Stage = PrintifyStages.Publishing, PublishRequestedAt = now };
        log.ApiResponse = state.Write();
        log.ErrorMessage = null;
        await uploads.UpdateAsync(log, saveChange: true, cancellationToken);
        return await SyncAsync(productId, printifyProductId, new SyncPrintifyProductRequestDto([], false), cancellationToken);
    }

    public async Task<Result<PrintifyProductDto>> SyncAsync(Guid productId, string printifyProductId, SyncPrintifyProductRequestDto request, CancellationToken cancellationToken = default)
    {
        var context = await ProductContextAsync(productId, printifyProductId, cancellationToken);
        if (context.IsFailure)
            return Result.Failure<PrintifyProductDto>(context.Error);
        var (access, log) = context.Value;
        var (integration, token, shopId, _) = access;

        PrintifyProduct product;
        IReadOnlyList<PrintifyShippingRate> shipping;
        try
        {
            product = await printify.GetProductAsync(token, shopId, printifyProductId, cancellationToken);
            shipping = await CachedShippingAsync(token, product, log, cancellationToken);
        }
        catch (PrintifyApiException exception)
        {
            return Result.Failure<PrintifyProductDto>(await FailureAsync(access, exception, cancellationToken));
        }

        var state = LogState.Read(log.ApiResponse);
        var warnings = new List<string>();
        var now = timeProvider.GetUtcNow();
        if (product.ExternalId is { } listingId)
        {
            state = state with { Stage = PrintifyStages.Published, EtsyListingId = listingId };
            if (!state.MediaAttempted && (request.MockupIds.Count > 0 || request.IncludeVideo))
            {
                var (attached, mediaWarnings) = await AttachMediaAsync(productId, listingId, request, product.Title, cancellationToken);
                state = state with { MediaAttempted = true, MediaAttached = attached };
                warnings.AddRange(mediaWarnings);
            }
        }
        else if (state.Stage == PrintifyStages.Publishing && !product.IsLocked && state.PublishRequestedAt is { } asked
            && now.UtcDateTime - asked > PublishGrace)
        {
            // Printify reports an Etsy refusal only on its own site, by unlocking the product without a listing.
            warnings.Add("Printify finished without creating the Etsy listing. Open the product on Printify to see why (often the Etsy shop currency or a price below Etsy's minimum), fix it, then publish again.");
            state = state with { Stage = PrintifyStages.Created };
        }

        if (state.Write() != log.ApiResponse)
        {
            log.ApiResponse = state.Write();
            await uploads.UpdateAsync(log, saveChange: true, cancellationToken);
        }
        return Result.Success(ToDto(productId, product, state, shipping, alreadyCreated: false, warnings));
    }

    /// <summary>Adds approved APCS mock-ups (in front of Printify's) and the video to Printify's Etsy listing.</summary>
    private async Task<(int Attached, IReadOnlyList<string> Warnings)> AttachMediaAsync(
        Guid productId, string listingId, SyncPrintifyProductRequestDto request, string title, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        if (currentUser.UserId is not Guid userId || await EtsyAccessAsync(userId, cancellationToken) is not { } connection)
        {
            warnings.Add("Connect Etsy on the Shop page to add the APCS mock-ups and video to this listing.");
            return (0, warnings);
        }

        var approved = (await ApprovedMockupsAsync(productId, cancellationToken)).ToDictionary(mockup => mockup.Id);
        var attached = 0;
        foreach (var (mockupId, index) in request.MockupIds.Where(approved.ContainsKey).Take(MaxAttachedImages).Select((id, index) => (id, index)))
        {
            var mockup = approved[mockupId];
            try
            {
                var image = await storage.ReadImageAsync(mockup.StorageKey, mockup.StorageVersion, cancellationToken);
                await etsy.UploadListingImageAsync(connection.AccessToken, connection.Integration.EtsyShopId, listingId, image,
                    $"apcs-mockup-{index + 1}.png", index + 1, title, cancellationToken);
                attached++;
            }
            catch (Exception exception) when (exception is EtsyApiException or HttpRequestException or IOException)
            {
                logger.LogWarning("Mock-up {Index} for Etsy listing {ListingId} failed: {Error}.", index + 1, listingId, exception.GetType().Name);
                warnings.Add($"Mock-up {index + 1} could not be added to the Etsy listing{(exception is EtsyApiException { Reason: { } reason } ? $": {reason}" : ".")}");
            }
        }
        if (request.IncludeVideo && await ApprovedVideoAsync(productId, cancellationToken) is { } video)
        {
            try
            {
                await etsy.UploadListingVideoAsync(connection.AccessToken, connection.Integration.EtsyShopId, listingId,
                    storage.SignRead(video.StorageKey!, "video", video.StorageVersion), title, cancellationToken);
                attached++;
            }
            catch (EtsyApiException exception)
            {
                logger.LogWarning("Video for Etsy listing {ListingId} failed: {Kind}.", listingId, exception.Kind);
                warnings.Add($"The video could not be added to the Etsy listing{(exception.Reason is { } reason ? $": {reason}" : ".")}");
            }
        }
        return (attached, warnings);
    }

    private async Task<(EtsyIntegration Integration, string AccessToken)?> EtsyAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            return await shops.ConnectedAccessAsync(userId, cancellationToken);
        }
        catch (EtsyApiException exception)
        {
            logger.LogWarning("Etsy access for user {UserId} failed: {Kind}.", userId, exception.Kind);
            return null;
        }
    }

    private async Task<Result<(PrintifyAccess Access, PrintifyUploadLog Log)>> ProductContextAsync(Guid productId, string printifyProductId, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<(PrintifyAccess, PrintifyUploadLog)>(ShopErrors.Unauthenticated());
        var access = await AccessAsync(cancellationToken);
        if (access.IsFailure)
            return Result.Failure<(PrintifyAccess, PrintifyUploadLog)>(access.Error);
        // Only a Printify product APCS made from this product, in the connected store, can be published or synced here.
        var log = (await uploads.FindAsync(row => row.ProductId == productId && row.PrintifyIntegrationId == access.Value.Integration.Id
                && row.PrintifyProductId == printifyProductId && row.UploadStatus == "success", cancellationToken))
            .OrderByDescending(row => row.AttemptedAt)
            .FirstOrDefault();
        return log is null
            ? Result.Failure<(PrintifyAccess, PrintifyUploadLog)>(ShopErrors.PrintifyProductNotFound())
            : Result.Success((access.Value, log));
    }

    /// <summary>The seller's usable Printify key and store, or why there is none (in words that point to the fix).</summary>
    private async Task<Result<PrintifyAccess>> AccessAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Result.Failure<PrintifyAccess>(ShopErrors.Unauthenticated());
        var (connection, access) = await ResolveAsync(userId, refresh: false, cancellationToken);
        return access is not null
            ? Result.Success(access)
            : Result.Failure<PrintifyAccess>(connection.Status switch
            {
                PrintifyStatuses.NotConnected => ShopErrors.PrintifyNotConnected(),
                PrintifyStatuses.NoStore => ShopErrors.PrintifyNoStore(),
                PrintifyStatuses.Unavailable => ShopErrors.PrintifyUnavailable(),
                _ => ShopErrors.PrintifyKeyInvalid(),
            });
    }

    /// <summary>
    /// A key refused mid-way is the seller's to fix on API Keys; the remembered store is dropped so the next call
    /// checks the key again instead of trusting the cache.
    /// </summary>
    private async Task<Error> FailureAsync(PrintifyAccess access, PrintifyApiException exception, CancellationToken cancellationToken)
    {
        logger.LogWarning("Printify call for integration {IntegrationId} failed: {Kind}.", access.Integration.Id, exception.Kind);
        if (exception.Kind == PrintifyFailureKind.Unauthorized)
            await cache.RemoveAsync(StoreKey(access.Integration.UserId, access.KeyId), cancellationToken);
        return ShopErrors.PrintifyRejected(exception.Kind, exception.Reason);
    }
    private async Task FailAsync(PrintifyUploadLog log, PrintifyApiException exception, CancellationToken cancellationToken)
    {
        var message = exception.Reason is { } reason ? $"Printify refused: {reason}" : exception.Message;
        log.UploadStatus = "failed";
        log.ErrorMessage = message.Length <= 2000 ? message : message[..2000];
        log.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        await uploads.UpdateAsync(log, saveChange: true, cancellationToken);
    }

    private async Task<(IReadOnlyList<PrintifyCatalogVariant> Variants, IReadOnlyList<PrintifyShippingRate> Shipping)> CatalogAsync(
        string token, long blueprintId, long providerId, CancellationToken cancellationToken)
    {
        var variants = await CachedAsync($"printify:catalog:{blueprintId}:{providerId}:variants:v1",
            () => printify.GetVariantsAsync(token, blueprintId, providerId, cancellationToken), cancellationToken);
        var shipping = await CachedAsync($"printify:catalog:{blueprintId}:{providerId}:shipping:v1",
            () => printify.GetShippingAsync(token, blueprintId, providerId, cancellationToken), cancellationToken);
        return (variants, shipping);
    }

    private async Task<IReadOnlyList<PrintifyShippingRate>> CachedShippingAsync(string token, PrintifyProduct product, PrintifyUploadLog log, CancellationToken cancellationToken)
    {
        var payload = Deserialize<CreatePayload>(log.UploadPayload);
        if (payload is null)
            return [];
        return (await CatalogAsync(token, payload.BlueprintId, payload.PrintProviderId, cancellationToken)).Shipping;
    }

    private async Task<IReadOnlyList<T>> CachedAsync<T>(string key, Func<Task<IReadOnlyList<T>>> load, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<T[]>(key, cancellationToken);
        if (cached is not null)
            return cached;
        var loaded = (await load()).ToArray();
        await cache.SetAsync(key, loaded, CatalogLifetime, cancellationToken);
        return loaded;
    }

    /// <summary>The US rate for one item and each extra one, over the given variants (the highest, to be safe).</summary>
    internal static (decimal First, decimal Additional)? UsShipping(IReadOnlyList<PrintifyShippingRate> rates, IEnumerable<long> variantIds)
    {
        var ids = variantIds.ToHashSet();
        var us = rates.Where(rate => rate.Countries.Any(IsUnitedStates) && (rate.VariantIds.Count == 0 || rate.VariantIds.Any(ids.Contains))).ToArray();
        if (us.Length == 0)
            us = rates.Where(rate => rate.Countries.Any(country => country.Equals("REST_OF_THE_WORLD", StringComparison.OrdinalIgnoreCase))).ToArray();
        return us.Length == 0
            ? null
            : (PrintifyPricing.FromCents(us.Max(rate => rate.FirstItemCents)), PrintifyPricing.FromCents(us.Max(rate => rate.AdditionalItemCents)));
    }

    private static PrintifyProductDto ToDto(Guid productId, PrintifyProduct product, LogState state, IReadOnlyList<PrintifyShippingRate> shipping,
        bool alreadyCreated, IReadOnlyList<string> warnings)
    {
        var enabled = product.Variants.Where(variant => variant.IsEnabled).ToArray();
        var us = UsShipping(shipping, enabled.Select(variant => variant.Id));
        var shippingCost = us?.First ?? 0;
        var listingId = product.ExternalId ?? state.EtsyListingId;
        return new PrintifyProductDto(
            productId, product.Id, product.Title,
            product.ExternalId is not null ? PrintifyStages.Published : state.Stage,
            listingId,
            listingId is null ? null : EtsyListingUrl(listingId),
            enabled.Select(variant =>
            {
                var cost = PrintifyPricing.FromCents(variant.CostCents);
                var price = PrintifyPricing.FromCents(variant.PriceCents);
                return new PrintifyProductVariantDto(variant.Id, variant.Title, cost, price, variant.IsEnabled,
                    PrintifyPricing.EstimatedFees(price, shippingCost), PrintifyPricing.EstimatedProfit(price, cost, shippingCost));
            }).ToArray(),
            us?.First, alreadyCreated, state.MediaAttached, warnings);
    }

    internal static IReadOnlyList<PrintifyBlueprintDto> RankBlueprints(IReadOnlyList<PrintifyBlueprint> blueprints, string? query)
    {
        var words = (string.IsNullOrWhiteSpace(query) ? "unisex tee" : query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string Haystack(PrintifyBlueprint blueprint) => $"{blueprint.Title} {blueprint.Brand} {blueprint.Model}";
        return blueprints
            .Where(blueprint => words.All(word => Haystack(blueprint).Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(blueprint => words.All(word => blueprint.Title.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(blueprint => blueprint.Title.Length)
            .Take(30)
            .Select(blueprint => new PrintifyBlueprintDto(blueprint.Id, blueprint.Title, blueprint.Brand, blueprint.Model, blueprint.ImageUrl))
            .ToArray();
    }

    private static bool IsUnitedStates(string? country) =>
        country is not null && (country.Equals("US", StringComparison.OrdinalIgnoreCase) || country.Equals("United States", StringComparison.OrdinalIgnoreCase)
            || country.Equals("USA", StringComparison.OrdinalIgnoreCase));

    private async Task<PrintifyProductSettingsDto> LastSettingsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var last = await uploads.Query()
            .Where(row => row.PrintifyIntegration.UserId == userId && row.SyncType == "create")
            .OrderByDescending(row => row.AttemptedAt)
            .Select(row => row.UploadPayload)
            .FirstOrDefaultAsync(cancellationToken);
        return Deserialize<CreatePayload>(last) is { } payload
            ? new PrintifyProductSettingsDto(payload.BlueprintId, payload.BlueprintTitle, payload.PrintProviderId, payload.PrintProviderTitle,
                payload.Colors, payload.Sizes, payload.Price)
            : new PrintifyProductSettingsDto(null, null, null, null, [], [], DefaultPrice);
    }

    private Task<Product?> OwnedProductAsync(Guid productId, Guid userId, CancellationToken cancellationToken) =>
        products.Query().Where(product => product.Id == productId && product.UserId == userId && product.DeletedAt == null)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<IReadOnlyList<MockupImage>> ApprovedMockupsAsync(Guid productId, CancellationToken cancellationToken) =>
        await mockups.Query()
            .Where(mockup => mockup.ProductId == productId && mockup.DeletedAt == null && mockup.ApprovalStatus == "approved"
                && mockup.ApprovedRevision == mockup.MetadataRevision)
            .OrderBy(mockup => mockup.CreatedAt)
            .Take(Validators.PublishEtsyDraftValidator.MaxImages)
            .ToListAsync(cancellationToken);

    private Task<PromoVideo?> ApprovedVideoAsync(Guid productId, CancellationToken cancellationToken) =>
        videos.Query()
            .Where(video => video.ProductId == productId && video.DeletedAt == null && video.ApprovalStatus == "approved" && video.StorageKey != null)
            .OrderByDescending(video => video.UpdatedAt ?? video.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32].ToLowerInvariant();

    private static string Truncate(string value) => value.Length <= 100 ? value : value[..100];

    private sealed record PrintifyAccess(PrintifyIntegration Integration, string Token, long ShopId, Guid KeyId);

    /// <summary>What a create push sent; also the next push's defaults.</summary>
    internal sealed record CreatePayload(
        Guid DesignId, long BlueprintId, string? BlueprintTitle, long PrintProviderId, string? PrintProviderTitle,
        IReadOnlyList<long> VariantIds, IReadOnlyList<string> Colors, IReadOnlyList<string> Sizes,
        string Title, string Description, IReadOnlyList<string> Tags, decimal Price, double Scale, string Position);

    /// <summary>How far a created product got, kept as the log row's <c>api_response</c>.</summary>
    internal sealed record LogState(string Stage, string? EtsyListingId, DateTime? PublishRequestedAt, int MediaAttached, bool MediaAttempted)
    {
        public static LogState Read(string? json) => Deserialize<LogState>(json) ?? new LogState(PrintifyStages.Created, null, null, 0, false);

        public string Write() => JsonSerializer.Serialize(this, Json);
    }
}

public static class PrintifyStatuses
{
    public const string NotConnected = "not_connected";
    public const string Connected = "connected";
    /// <summary>The key on API Keys is disabled, expired, failed its last check, or Printify refuses it.</summary>
    public const string KeyInvalid = "key_invalid";
    /// <summary>The key works but the Printify account has no store.</summary>
    public const string NoStore = "no_store";
    /// <summary>Printify could not be reached; the key itself may be fine.</summary>
    public const string Unavailable = "unavailable";
}

public static class PrintifyStages
{
    /// <summary>On Printify only.</summary>
    public const string Created = "created";
    /// <summary>Printify was asked to publish to Etsy and has not finished.</summary>
    public const string Publishing = "publishing";
    /// <summary>The Etsy listing exists.</summary>
    public const string Published = "published";
}
