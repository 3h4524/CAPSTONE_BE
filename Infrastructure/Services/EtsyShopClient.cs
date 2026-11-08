using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using APCS.Application.Abstractions.Commerce;
using APCS.Infrastructure.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Etsy Open API v3 adapter for a seller's own shop: OAuth 2.0 with PKCE, token refresh and shop reads.
/// Tokens never reach a log; failures surface as <see cref="EtsyApiException"/> without response bodies.
/// </summary>
public sealed class EtsyShopClient(
    IHttpClientFactory clients,
    IOptions<EtsyOptions> options,
    IOptions<AppOptions> appOptions,
    IDataProtectionProvider protection,
    TimeProvider timeProvider,
    ILogger<EtsyShopClient> logger) : IEtsyShopClient
{
    public const string HttpClientName = "EtsyShop";

    /// <summary>The same Etsy client with a longer timeout, for image and video uploads.</summary>
    public const string UploadClientName = "EtsyShopUpload";

    private readonly EtsyOptions etsy = options.Value;
    private readonly IDataProtector protector = protection.CreateProtector("APCS.EtsyTokens.v1");

    public bool IsConfigured => etsy.IsConfigured;

    public string BuildAuthorizationUrl(string state, string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = etsy.ApiKey,
            ["redirect_uri"] = etsy.OAuthRedirectUri,
            ["scope"] = etsy.OAuthScopes,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        return $"{etsy.OAuthAuthorizeUrl}?{string.Join('&', query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"))}";
    }

    public string BuildReturnUrl(string outcome)
    {
        var baseUrl = appOptions.Value.BaseUrl.TrimEnd('/');
        var path = etsy.ReturnPagePath.StartsWith('/') ? etsy.ReturnPagePath : $"/{etsy.ReturnPagePath}";
        return $"{baseUrl}{path}?etsy={Uri.EscapeDataString(outcome)}";
    }

    public Task<EtsyTokenSet> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken) =>
        RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = etsy.ApiKey,
            ["redirect_uri"] = etsy.OAuthRedirectUri,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
        }, cancellationToken);

    public Task<EtsyTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = etsy.ApiKey,
            ["refresh_token"] = refreshToken,
        }, cancellationToken);

    public async Task<string?> GetOwnShopIdAsync(string accessToken, CancellationToken cancellationToken)
    {
        // Etsy answers 404 "Could not find a shop for User" for an account without a shop, rather than
        // an empty shop_id: that is the no-shop case, not a failure.
        using var request = new HttpRequestMessage(HttpMethod.Get, "users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var document = await SendAsync(request, cancellationToken, notFoundIsEmpty: true);
        return document is not null
            && document.RootElement.TryGetProperty("shop_id", out var shopId) && shopId.ValueKind is JsonValueKind.Number or JsonValueKind.String
            ? shopId.ToString()
            : null;
    }

    public async Task<EtsyShopDetails> GetShopAsync(string accessToken, string shopId, CancellationToken cancellationToken)
    {
        using var document = await GetAsync($"shops/{Uri.EscapeDataString(shopId)}", accessToken, cancellationToken);
        var shop = document.RootElement;
        var openedAt = Number(shop, "create_date") is { } seconds and > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : (DateTimeOffset?)null;
        return new EtsyShopDetails(
            Text(shop, "shop_id") ?? shopId,
            Text(shop, "shop_name") ?? shopId,
            Text(shop, "title"),
            Text(shop, "url"),
            Text(shop, "icon_url_fullxfull"),
            Text(shop, "currency_code"),
            (int)(Number(shop, "listing_active_count") ?? 0),
            (int)(Number(shop, "digital_listing_count") ?? 0),
            (int)(Number(shop, "num_favorers") ?? 0),
            (int)(Number(shop, "transaction_sold_count") ?? 0),
            shop.TryGetProperty("review_average", out var average) && average.ValueKind == JsonValueKind.Number ? Math.Round(average.GetDecimal(), 2) : null,
            (int)(Number(shop, "review_count") ?? 0),
            Flag(shop, "is_vacation"),
            openedAt,
            Text(shop, "image_url_760x100"),
            Text(shop, "announcement") is { Length: > 0 } announcement ? announcement : null,
            // Structured return policies, or a free-text refund policy, both count as having one.
            Flag(shop, "is_using_structured_policies") || !string.IsNullOrWhiteSpace(Text(shop, "policy_refunds")),
            Flag(shop, "is_etsy_payments_onboarded"),
            Flag(shop, "accepts_custom_requests"),
            Text(shop, "shipping_from_country_iso"));
    }

    public async Task<EtsyListingPage> GetListingsAsync(string accessToken, string shopId, string state, int limit, bool withImages, CancellationToken cancellationToken)
    {
        var query = $"state={Uri.EscapeDataString(state)}&limit={Math.Clamp(limit, 1, 100)}&sort_on=updated&sort_order=desc"
            + (withImages ? "&includes=Images" : string.Empty);
        using var document = await GetAsync($"shops/{Uri.EscapeDataString(shopId)}/listings?{query}", accessToken, cancellationToken);
        var root = document.RootElement;
        var listings = Results(root).Select(listing => new EtsyListing(
            Text(listing, "listing_id") ?? string.Empty,
            Text(listing, "title") ?? string.Empty,
            Text(listing, "url"),
            Text(listing, "state") ?? state,
            Money(listing, "price"),
            (int)(Number(listing, "quantity") ?? 0),
            (int)(Number(listing, "views") ?? 0),
            (int)(Number(listing, "num_favorers") ?? 0),
            Strings(listing, "tags"),
            listing.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array ? images.GetArrayLength() : 0,
            listing.TryGetProperty("images", out images) && images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0
                ? Text(images[0], "url_570xN") ?? Text(images[0], "url_170x135")
                : null,
            Time(listing, "created_timestamp"),
            Time(listing, "updated_timestamp"),
            Time(listing, "ending_timestamp"))).ToArray();
        return new EtsyListingPage((int)(Number(root, "count") ?? listings.Length), listings);
    }

    public async Task<IReadOnlyList<EtsyReceipt>> GetReceiptsAsync(string accessToken, string shopId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxReceipts, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var receipts = new List<EtsyReceipt>();
        for (var offset = 0; receipts.Count < maxReceipts; offset += pageSize)
        {
            var query = $"min_created={fromUtc.ToUnixTimeSeconds()}&max_created={toUtc.ToUnixTimeSeconds()}&limit={pageSize}&offset={offset}&sort_on=created&sort_order=desc";
            using var document = await GetAsync($"shops/{Uri.EscapeDataString(shopId)}/receipts?{query}", accessToken, cancellationToken);
            var page = Results(document.RootElement).Select(ReadReceipt).ToArray();
            receipts.AddRange(page);
            if (page.Length < pageSize)
                break;
        }
        return receipts.Take(maxReceipts).ToArray();
    }

    public async Task<EtsyPayment?> GetReceiptPaymentAsync(string accessToken, string shopId, string receiptId, CancellationToken cancellationToken)
    {
        using var document = await GetAsync($"shops/{Uri.EscapeDataString(shopId)}/receipts/{Uri.EscapeDataString(receiptId)}/payments", accessToken, cancellationToken);
        var payment = Results(document.RootElement).FirstOrDefault();
        return payment.ValueKind == JsonValueKind.Object
            ? new EtsyPayment(Money(payment, "amount_gross"), Money(payment, "amount_fees"), Money(payment, "amount_net"))
            : null;
    }

    public async Task<IReadOnlyList<EtsyReview>> GetReviewsAsync(string accessToken, string shopId, int limit, CancellationToken cancellationToken)
    {
        using var document = await GetAsync($"shops/{Uri.EscapeDataString(shopId)}/reviews?limit={Math.Clamp(limit, 1, 100)}", accessToken, cancellationToken);
        return Results(document.RootElement)
            .Select(review => new EtsyReview(
                (int)(Number(review, "rating") ?? 0),
                Text(review, "review") is { Length: > 0 } text ? text : null,
                Text(review, "listing_id"),
                Time(review, "created_timestamp")))
            .Where(review => review.Rating is >= 1 and <= 5)
            .ToArray();
    }

    public async Task<EtsyShopSetup> GetShopSetupAsync(string accessToken, string shopId, CancellationToken cancellationToken)
    {
        var shop = Uri.EscapeDataString(shopId);
        return new EtsyShopSetup(
            await CountAsync($"shops/{shop}/shipping-profiles", accessToken, cancellationToken),
            await CountAsync($"shops/{shop}/production-partners", accessToken, cancellationToken),
            await CountAsync($"shops/{shop}/sections", accessToken, cancellationToken));
    }

    public async Task<IReadOnlyList<EtsyOption>> GetShippingProfilesAsync(string accessToken, string shopId, CancellationToken cancellationToken)
    {
        using var document = await GetAsync($"shops/{Uri.EscapeDataString(shopId)}/shipping-profiles", accessToken, cancellationToken);
        return Results(document.RootElement)
            .Where(profile => !Flag(profile, "is_deleted"))
            .Select(profile => new EtsyOption(Number(profile, "shipping_profile_id") ?? 0, Text(profile, "title") ?? "Shipping profile"))
            .Where(option => option.Id > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<EtsyOption>> GetProcessingProfilesAsync(string accessToken, string shopId, CancellationToken cancellationToken)
    {
        using var document = await GetAsync($"shops/{Uri.EscapeDataString(shopId)}/readiness-state-definitions", accessToken, cancellationToken);
        return Results(document.RootElement)
            .Select(profile =>
            {
                var state = Text(profile, "readiness_state") == "made_to_order" ? "Made to order" : "Ready to ship";
                var days = Text(profile, "processing_days_display_label")
                    ?? $"{Number(profile, "min_processing_days") ?? 0}–{Number(profile, "max_processing_days") ?? 0} days";
                return new EtsyOption(Number(profile, "readiness_state_id") ?? Number(profile, "id") ?? 0, $"{state} · {days}");
            })
            .Where(option => option.Id > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<EtsyOption>> GetProductionPartnersAsync(string accessToken, string shopId, CancellationToken cancellationToken)
    {
        using var document = await GetAsync($"shops/{Uri.EscapeDataString(shopId)}/production-partners", accessToken, cancellationToken);
        return Results(document.RootElement)
            .Select(partner => new EtsyOption(Number(partner, "production_partner_id") ?? 0,
                string.Join(" · ", new[] { Text(partner, "partner_name"), Text(partner, "location") }.Where(part => !string.IsNullOrWhiteSpace(part)))))
            .Where(option => option.Id > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<EtsyTaxonomyNode>> GetSellerTaxonomyAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "seller-taxonomy/nodes");
        using var document = (await SendAsync(request, cancellationToken))!;
        var nodes = new List<EtsyTaxonomyNode>();
        void Walk(JsonElement node, string parentPath)
        {
            var name = Text(node, "name") ?? string.Empty;
            var path = parentPath.Length == 0 ? name : $"{parentPath} > {name}";
            if (Number(node, "id") is { } id and > 0)
                nodes.Add(new EtsyTaxonomyNode(id, path));
            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                foreach (var child in children.EnumerateArray())
                    Walk(child, path);
        }
        foreach (var root in Results(document.RootElement))
            Walk(root, string.Empty);
        return nodes;
    }

    public async Task<EtsyCreatedListing> CreateDraftListingAsync(string accessToken, string shopId, EtsyDraftListing listing, CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("title", listing.Title),
            new("description", listing.Description),
            new("price", listing.Price.ToString("0.00", CultureInfo.InvariantCulture)),
            new("quantity", listing.Quantity.ToString(CultureInfo.InvariantCulture)),
            new("taxonomy_id", listing.TaxonomyId.ToString(CultureInfo.InvariantCulture)),
            new("type", listing.Type),
            new("who_made", listing.WhoMade),
            new("when_made", listing.WhenMade),
            new("is_supply", "false"),
        };
        // Etsy reads list fields of a form body as comma-separated values.
        if (listing.Tags.Count > 0) form.Add(new("tags", string.Join(',', listing.Tags)));
        if (listing.ShippingProfileId is { } shipping) form.Add(new("shipping_profile_id", shipping.ToString(CultureInfo.InvariantCulture)));
        if (listing.ReadinessStateId is { } readiness) form.Add(new("readiness_state_id", readiness.ToString(CultureInfo.InvariantCulture)));
        if (listing.ProductionPartnerIds.Count > 0)
            form.Add(new("production_partner_ids", string.Join(',', listing.ProductionPartnerIds.Select(id => id.ToString(CultureInfo.InvariantCulture)))));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"shops/{Uri.EscapeDataString(shopId)}/listings") { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var document = (await SendAsync(request, cancellationToken))!;
        var listingId = Text(document.RootElement, "listing_id")
            ?? throw new EtsyApiException(EtsyFailureKind.Unavailable, "Etsy returned no listing id.");
        return new EtsyCreatedListing(listingId, Text(document.RootElement, "url"));
    }

    public async Task UploadListingImageAsync(string accessToken, string shopId, string listingId, byte[] image, string fileName, int rank, string? altText, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png");
        content.Add(file, "image", fileName);
        content.Add(new StringContent(rank.ToString(CultureInfo.InvariantCulture)), "rank");
        if (!string.IsNullOrWhiteSpace(altText)) content.Add(new StringContent(altText.Length <= 500 ? altText : altText[..500]), "alt_text");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"shops/{Uri.EscapeDataString(shopId)}/listings/{Uri.EscapeDataString(listingId)}/images") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var _ = await SendAsync(request, cancellationToken, clientName: UploadClientName);
    }

    public async Task UploadListingVideoAsync(string accessToken, string shopId, string listingId, string sourceUrl, string name, CancellationToken cancellationToken)
    {
        byte[] video;
        try
        {
            // The media store signs a short-lived URL; the video is read once and passed straight to Etsy.
            video = await clients.CreateClient(MediaDownloadClientName).GetByteArrayAsync(sourceUrl, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new EtsyApiException(EtsyFailureKind.Unavailable, "The video could not be read from storage.");
        }

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(video);
        file.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        content.Add(file, "video", "video.mp4");
        content.Add(new StringContent(name.Length <= 100 ? name : name[..100]), "name");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"shops/{Uri.EscapeDataString(shopId)}/listings/{Uri.EscapeDataString(listingId)}/videos") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var _ = await SendAsync(request, cancellationToken, clientName: UploadClientName);
    }

    public async Task UploadListingFileAsync(string accessToken, string shopId, string listingId, string sourceUrl, string fileName, int rank, CancellationToken cancellationToken)
    {
        byte[] file;
        try
        {
            file = await clients.CreateClient(MediaDownloadClientName).GetByteArrayAsync(sourceUrl, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new EtsyApiException(EtsyFailureKind.Unavailable, "The design file could not be read from storage.");
        }

        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png");
        content.Add(part, "file", fileName);
        content.Add(new StringContent(fileName), "name");
        content.Add(new StringContent(rank.ToString(CultureInfo.InvariantCulture)), "rank");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"shops/{Uri.EscapeDataString(shopId)}/listings/{Uri.EscapeDataString(listingId)}/files") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var _ = await SendAsync(request, cancellationToken, clientName: UploadClientName);
    }

    /// <summary>Plain client for reading stored media by signed URL; it carries no Etsy key.</summary>
    public const string MediaDownloadClientName = "EtsyMediaDownload";

    private async Task<int> CountAsync(string path, string accessToken, CancellationToken cancellationToken)
    {
        using var document = await GetAsync(path, accessToken, cancellationToken);
        return (int)(Number(document.RootElement, "count") ?? Results(document.RootElement).Count());
    }

    private static EtsyReceipt ReadReceipt(JsonElement receipt)
    {
        var items = receipt.TryGetProperty("transactions", out var transactions) && transactions.ValueKind == JsonValueKind.Array
            ? transactions.EnumerateArray().Select(item => new EtsyReceiptItem(
                Text(item, "listing_id"),
                Text(item, "title") ?? string.Empty,
                (int)(Number(item, "quantity") ?? 0),
                Money(item, "price"),
                Time(item, "paid_timestamp"),
                Time(item, "shipped_timestamp"),
                Time(item, "expected_ship_date"))).ToArray()
            : [];
        EtsyMoney? refunded = null;
        if (receipt.TryGetProperty("refunds", out var refunds) && refunds.ValueKind == JsonValueKind.Array)
            foreach (var refund in refunds.EnumerateArray())
                if (Money(refund, "amount") is { } amount)
                    refunded = refunded is null ? amount : refunded with { Amount = refunded.Amount + amount.Amount };
        return new EtsyReceipt(
            Text(receipt, "receipt_id") ?? string.Empty,
            Text(receipt, "status") ?? "unknown",
            Flag(receipt, "is_paid"),
            Flag(receipt, "is_shipped"),
            Time(receipt, "created_timestamp") ?? DateTimeOffset.UnixEpoch,
            Text(receipt, "country_iso"),
            Money(receipt, "grandtotal"),
            refunded,
            items);
    }

    private static IEnumerable<JsonElement> Results(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray()
            : [];

    /// <summary>Etsy money is <c>{ amount, divisor, currency_code }</c>: 2499 over 100 is 24.99.</summary>
    private static EtsyMoney? Money(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var money) || money.ValueKind != JsonValueKind.Object)
            return null;
        var amount = Number(money, "amount");
        var divisor = Number(money, "divisor") is { } value and > 0 ? value : 1;
        var currency = Text(money, "currency_code");
        return amount is null || currency is null ? null : new EtsyMoney((decimal)amount.Value / divisor, currency);
    }

    private static DateTimeOffset? Time(JsonElement element, string name) =>
        Number(element, name) is { } seconds and > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToArray()
            : [];

    public string Protect(string value) => protector.Protect(value);

    public string Unprotect(string value) => protector.Unprotect(value);

    private async Task<EtsyTokenSet> RequestTokensAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, etsy.OAuthTokenUrl) { Content = new FormUrlEncodedContent(form) };
        using var document = (await SendAsync(request, cancellationToken))!;
        var root = document.RootElement;
        var accessToken = Text(root, "access_token");
        var refreshToken = Text(root, "refresh_token");
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            throw new EtsyApiException(EtsyFailureKind.Unavailable, "Etsy returned no token.");
        var lifetime = Number(root, "expires_in") is { } seconds and > 0 ? seconds : 3600;
        return new EtsyTokenSet(accessToken, refreshToken, timeProvider.GetUtcNow().AddSeconds(lifetime));
    }

    private async Task<JsonDocument> GetAsync(string path, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return (await SendAsync(request, cancellationToken))!;
    }

    /// <summary>Sends a call and parses its JSON; with <paramref name="notFoundIsEmpty"/> a 404 returns null.</summary>
    private async Task<JsonDocument?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, bool notFoundIsEmpty = false, string clientName = HttpClientName)
    {
        HttpResponseMessage response;
        try
        {
            response = await clients.CreateClient(clientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new EtsyApiException(EtsyFailureKind.Unavailable, "Etsy could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EtsyApiException(EtsyFailureKind.Unavailable, "Etsy did not answer in time.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (notFoundIsEmpty && response.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
            {
                var kind = Classify(response.StatusCode, body);
                // Only the status and Etsy's error code: the body of a token call can echo request values.
                logger.LogWarning("Etsy {Method} {Path} returned {Status} ({Kind}, {Error}).",
                    request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode, kind, ErrorCode(body) ?? "no code");
                // Etsy's `error` text explains a refused listing (a bad shipping profile, a price below the minimum).
                // The token endpoint is excluded: its errors are codes, and nothing of the request should travel on.
                var reason = request.RequestUri?.AbsoluteUri == etsy.OAuthTokenUrl ? null : ErrorCode(body);
                throw new EtsyApiException(kind, $"Etsy returned {(int)response.StatusCode}.", reason is { Length: > 300 } ? reason[..300] : reason);
            }
            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                throw new EtsyApiException(EtsyFailureKind.Unavailable, "Etsy returned an unreadable answer.");
            }
        }
    }

    /// <summary>
    /// An expired, revoked or mismatched grant is the seller's to fix by connecting again; Etsy reports it as
    /// 401/403, or as 400 <c>invalid_grant</c> on the token endpoint.
    /// </summary>
    internal static EtsyFailureKind Classify(HttpStatusCode status, string body) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => EtsyFailureKind.Unauthorized,
        HttpStatusCode.BadRequest when ErrorCode(body) is "invalid_grant" => EtsyFailureKind.Unauthorized,
        HttpStatusCode.TooManyRequests => EtsyFailureKind.RateLimited,
        _ => EtsyFailureKind.Unavailable,
    };

    private static string? ErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? Text(document.RootElement, "error") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String
                && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : null;
}
