using System.Globalization;
using System.Net;
using System.Text.Json;
using APCS.Application.Abstractions.Commerce;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>Reads public active Etsy listings for market research.</summary>
public sealed class EtsyMarketDataConnector(
    IHttpClientFactory clients,
    IOptions<EtsyOptions> options,
    ILogger<EtsyMarketDataConnector> logger) : ICommerceMarketDataConnector
{
    internal const string HttpClientName = "EtsyMarketResearch";
    private const int MaxRequestAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
    private readonly EtsyOptions settings = options.Value;

    public CommercePlatform Platform => CommercePlatform.Etsy;

    public async Task<IReadOnlyList<CommerceMarketListing>> SearchPublicListingsAsync(
        CommerceMarketSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!settings.IsConfigured)
            throw new InvalidOperationException("Etsy public API is not configured. Set Etsy__ApiKey.");

        var results = new List<CommerceMarketListing>();
        foreach (var query in request.Queries.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var uri = $"listings/active?limit={Math.Clamp(request.LimitPerQuery, 1, 100)}&offset=0&keywords={Uri.EscapeDataString(query)}&sort_on=score&sort_order=down";
            using var response = await GetWithRetryAsync(uri, query, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                throw new HttpRequestException("Etsy rejected the API credentials.", null, response.StatusCode);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (settings.LogRawResponses)
            {
                var maxChars = Math.Clamp(settings.RawResponseLogMaxChars, 1000, 100_000);
                var preview = responseBody.Length > maxChars
                    ? $"{responseBody[..maxChars]}...[truncated {responseBody.Length - maxChars} chars]"
                    : responseBody;
                logger.LogInformation(
                    "Etsy raw response for query {Query} (HTTP {StatusCode}): {ResponseBody}",
                    query,
                    (int)response.StatusCode,
                    preview);
            }

            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("results", out var listings) || listings.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var listing in listings.EnumerateArray())
            {
                var mapped = Map(listing, query);
                if (mapped is not null)
                    results.Add(mapped);
            }
        }

        return results;
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(
        string uri,
        string query,
        CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(HttpClientName);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await client.GetAsync(uri, cancellationToken);
            }
            catch (HttpRequestException exception) when (exception.StatusCode is null && attempt < MaxRequestAttempts)
            {
                logger.LogWarning(
                    exception,
                    "Etsy request failed for query {Query}; retrying attempt {Attempt} of {MaxAttempts}.",
                    query,
                    attempt + 1,
                    MaxRequestAttempts);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    private static CommerceMarketListing? Map(JsonElement item, string matchedQuery)
    {
        var id = GetString(item, "listing_id");
        var title = GetString(item, "title");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            return null;

        var tags = item.TryGetProperty("tags", out var tagArray) && tagArray.ValueKind == JsonValueKind.Array
            ? tagArray.EnumerateArray().Select(tag => tag.ValueKind == JsonValueKind.String ? tag.GetString() : null).Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag!).ToArray()
            : [];
        return new CommerceMarketListing(
            CommercePlatform.Etsy,
            id,
            GetString(item, "user_id") ?? GetString(item, "shop_id"),
            WebUtility.HtmlDecode(title),
            GetString(item, "description"),
            GetString(item, "url"),
            GetMoneyAmount(item, "price"),
            GetMoneyCurrency(item, "price") ?? GetString(item, "currency_code"),
            GetInt(item, "quantity"),
            GetInt(item, "num_favorers"),
            GetInt(item, "views"),
            UnixTime(GetLong(item, "original_creation_timestamp")),
            UnixTime(GetLong(item, "last_modified_timestamp")),
            tags,
            matchedQuery);
    }

    private static string? GetString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static int? GetInt(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;

    private static long? GetLong(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : null;

    private static decimal? GetMoneyAmount(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var direct))
            return direct;
        if (value.ValueKind != JsonValueKind.Object)
            return null;

        var amount = value.TryGetProperty("amount", out var amountValue) && amountValue.TryGetDecimal(out var rawAmount)
            ? rawAmount
            : (decimal?)null;
        var divisor = value.TryGetProperty("divisor", out var divisorValue) && divisorValue.TryGetDecimal(out var rawDivisor)
            ? rawDivisor
            : 1m;
        return amount is null || divisor == 0 ? null : amount / divisor;
    }

    private static string? GetMoneyCurrency(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("currency_code", out var currency)
            && currency.ValueKind == JsonValueKind.String
            ? currency.GetString()
            : null;

    private static DateTimeOffset? UnixTime(long? value) => value is null ? null : DateTimeOffset.FromUnixTimeSeconds(value.Value);
}
