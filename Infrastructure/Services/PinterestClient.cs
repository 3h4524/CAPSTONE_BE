using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using APCS.Application.Abstractions.Social;
using APCS.Infrastructure.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Pinterest API v5 adapter for a seller's own account: OAuth 2.0, token refresh, boards and image pins.
/// Tokens never reach a log; failures surface as <see cref="PinterestApiException"/> without response bodies.
/// </summary>
public sealed class PinterestClient(
    IHttpClientFactory clients,
    IOptions<PinterestOptions> options,
    IOptions<AppOptions> appOptions,
    IDataProtectionProvider protection,
    TimeProvider timeProvider,
    ILogger<PinterestClient> logger) : IPinterestClient
{
    public const string HttpClientName = "Pinterest";

    /// <summary>Boards read per page; Pinterest allows up to 250.</summary>
    internal const int BoardPageSize = 250;

    /// <summary>Pages of boards read at most, so an account with thousands of boards stays one quick call.</summary>
    internal const int MaxBoardPages = 4;

    private readonly PinterestOptions pinterest = options.Value;
    private readonly IDataProtector protector = protection.CreateProtector("APCS.PinterestTokens.v1");

    public bool IsConfigured => pinterest.IsConfigured;

    public string BuildAuthorizationUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = pinterest.AppId,
            ["redirect_uri"] = pinterest.OAuthRedirectUri,
            ["response_type"] = "code",
            ["scope"] = pinterest.OAuthScopes,
            ["state"] = state,
        };
        return $"{pinterest.OAuthAuthorizeUrl}?{string.Join('&', query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"))}";
    }

    public string BuildReturnUrl(string outcome)
    {
        var baseUrl = appOptions.Value.BaseUrl.TrimEnd('/');
        var path = pinterest.ReturnPagePath.StartsWith('/') ? pinterest.ReturnPagePath : $"/{pinterest.ReturnPagePath}";
        return $"{baseUrl}{path}?pinterest={Uri.EscapeDataString(outcome)}";
    }

    public Task<PinterestTokenSet> ExchangeCodeAsync(string code, CancellationToken cancellationToken) =>
        RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = pinterest.OAuthRedirectUri,
        }, cancellationToken);

    public Task<PinterestTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, cancellationToken);

    public async Task<string> GetUsernameAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, "user_account", accessToken);
        using var document = await SendAsync(request, cancellationToken);
        return Text(document.RootElement, "username") is { Length: > 0 } username
            ? username
            : throw new PinterestApiException(PinterestFailureKind.Unavailable, "Pinterest returned no username.");
    }

    public async Task<IReadOnlyList<PinterestBoard>> GetBoardsAsync(string accessToken, CancellationToken cancellationToken)
    {
        var boards = new List<PinterestBoard>();
        string? bookmark = null;
        for (var page = 0; page < MaxBoardPages; page++)
        {
            var path = $"boards?page_size={BoardPageSize}" + (bookmark is null ? string.Empty : $"&bookmark={Uri.EscapeDataString(bookmark)}");
            using var request = Authorized(HttpMethod.Get, path, accessToken);
            using var document = await SendAsync(request, cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                boards.AddRange(items.EnumerateArray()
                    .Where(item => Text(item, "id") is { Length: > 0 })
                    .Select(item => new PinterestBoard(Text(item, "id")!, Text(item, "name") ?? Text(item, "id")!, Text(item, "privacy"))));
            bookmark = Text(root, "bookmark");
            if (string.IsNullOrEmpty(bookmark))
                break;
        }
        return boards;
    }

    public async Task<PinterestCreatedPin> CreatePinAsync(string accessToken, PinterestNewPin pin, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["board_id"] = pin.BoardId,
            ["media_source"] = new Dictionary<string, string> { ["source_type"] = "image_url", ["url"] = pin.ImageUrl },
        };
        if (!string.IsNullOrWhiteSpace(pin.Title)) body["title"] = pin.Title;
        if (!string.IsNullOrWhiteSpace(pin.Description)) body["description"] = pin.Description;
        if (!string.IsNullOrWhiteSpace(pin.Link)) body["link"] = pin.Link;
        if (!string.IsNullOrWhiteSpace(pin.AltText)) body["alt_text"] = pin.AltText;

        using var request = Authorized(HttpMethod.Post, "pins", accessToken);
        request.Content = JsonContent.Create(body);
        using var document = await SendAsync(request, cancellationToken);
        var id = Text(document.RootElement, "id");
        if (string.IsNullOrEmpty(id))
            throw new PinterestApiException(PinterestFailureKind.Unavailable, "Pinterest returned no pin id.");
        return new PinterestCreatedPin(id, $"{pinterest.PinUrlBase.TrimEnd('/')}/{Uri.EscapeDataString(id)}/");
    }

    public string Protect(string value) => protector.Protect(value);

    public string Unprotect(string value) => protector.Unprotect(value);

    private async Task<PinterestTokenSet> RequestTokensAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, pinterest.OAuthTokenUrl) { Content = new FormUrlEncodedContent(form) };
        // Pinterest authenticates the app with HTTP Basic: the app id and secret, never sent in the form.
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{pinterest.AppId}:{pinterest.AppSecret}")));
        using var document = await SendAsync(request, cancellationToken);
        var root = document.RootElement;
        var accessToken = Text(root, "access_token");
        if (string.IsNullOrEmpty(accessToken))
            throw new PinterestApiException(PinterestFailureKind.Unavailable, "Pinterest returned no token.");
        var now = timeProvider.GetUtcNow();
        var lifetime = Number(root, "expires_in") is { } seconds and > 0 ? seconds : 30L * 24 * 3600;
        DateTimeOffset? refreshExpiresAt = Number(root, "refresh_token_expires_in") is { } refreshSeconds and > 0
            ? now.AddSeconds(refreshSeconds)
            : Number(root, "refresh_token_expires_at") is { } refreshAt and > 0 ? DateTimeOffset.FromUnixTimeSeconds(refreshAt) : null;
        return new PinterestTokenSet(accessToken, Text(root, "refresh_token") is { Length: > 0 } refresh ? refresh : null,
            now.AddSeconds(lifetime), refreshExpiresAt);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await clients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new PinterestApiException(PinterestFailureKind.Unavailable, "Pinterest could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PinterestApiException(PinterestFailureKind.Unavailable, "Pinterest did not answer in time.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenCall = request.RequestUri?.AbsoluteUri == pinterest.OAuthTokenUrl;
            if (!response.IsSuccessStatusCode)
            {
                var kind = Classify(response.StatusCode, tokenCall);
                // Only the status and Pinterest's numeric code: the body of a token call can echo request values.
                logger.LogWarning("Pinterest {Method} {Path} returned {Status} ({Kind}, code {Code}).",
                    request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode, kind, ErrorField(body, "code") ?? "none");
                var reason = tokenCall ? null : ErrorField(body, "message");
                throw new PinterestApiException(kind, $"Pinterest returned {(int)response.StatusCode}.", reason is { Length: > 300 } ? reason[..300] : reason);
            }
            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                throw new PinterestApiException(PinterestFailureKind.Unavailable, "Pinterest returned an unreadable answer.");
            }
        }
    }

    /// <summary>
    /// An expired or revoked grant is the seller's to fix by connecting again: 401/403, or a refused refresh on the
    /// token endpoint. Other 4xx answers refuse this one request.
    /// </summary>
    internal static PinterestFailureKind Classify(HttpStatusCode status, bool tokenCall) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PinterestFailureKind.Unauthorized,
        HttpStatusCode.BadRequest when tokenCall => PinterestFailureKind.Unauthorized,
        HttpStatusCode.TooManyRequests => PinterestFailureKind.RateLimited,
        >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError => PinterestFailureKind.Rejected,
        _ => PinterestFailureKind.Unavailable,
    };

    private static string? ErrorField(string body, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? Text(document.RootElement, name) : null;
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
