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
    private const string SortOn = "score";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
    private readonly EtsyOptions settings = options.Value;

    public CommercePlatform Platform => CommercePlatform.Etsy;

    public async Task<CommerceMarketSearchResult> SearchPublicListingsAsync(
        CommerceMarketSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!settings.IsConfigured)
            throw new InvalidOperationException("Etsy public API is not configured. Set Etsy__ApiKey.");

        var results = new List<CommerceMarketListing>();
        var queryResults = new List<CommerceMarketQueryResult>();
        var limitPerQuery = Math.Clamp(request.LimitPerQuery, 1, 500);
        foreach (var query in request.Queries
                     .Where(item => !string.IsNullOrWhiteSpace(item))
                     .Select(NormalizeQuery)
                     .Where(item => item.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var offset = 0;
            var collected = 0;
            var sampledBefore = results.Count;
            int? totalCount = null;
            while (collected < limitPerQuery)
            {
                var pageSize = Math.Min(100, limitPerQuery - collected);
                var uri = $"listings/active?limit={pageSize}&offset={offset}&keywords={Uri.EscapeDataString(query)}&sort_on={SortOn}&sort_order=down";
                using var response = await GetWithRetryAsync(uri, query, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    throw new HttpRequestException("Etsy rejected the API credentials.", null, response.StatusCode);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                LogRawResponse(query, response.StatusCode, responseBody);

                using var document = JsonDocument.Parse(responseBody);
                // The first page's count is the marketplace total for the query; later pages repeat it.
                totalCount ??= GetInt(document.RootElement, "count");
                if (!document.RootElement.TryGetProperty("results", out var listings) || listings.ValueKind != JsonValueKind.Array)
                    break;

                var pageCount = listings.GetArrayLength();
                if (pageCount == 0)
                    break;

                var rank = offset + 1;
                foreach (var listing in listings.EnumerateArray())
                {
                    var mapped = Map(listing, query, rank++);
                    if (mapped is not null)
                        results.Add(mapped);
                }

                collected += pageCount;
                offset += pageCount;
                if (pageCount < pageSize || (totalCount is not null && offset >= totalCount))
                    break;
            }

            queryResults.Add(new CommerceMarketQueryResult(
                query,
                totalCount,
                results.Count - sampledBefore,
                limitPerQuery,
                SortOn));
        }

        return new CommerceMarketSearchResult(results, queryResults);
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

    private void LogRawResponse(string query, HttpStatusCode statusCode, string responseBody)
    {
        if (!settings.LogRawResponses)
            return;

        var maxChars = Math.Clamp(settings.RawResponseLogMaxChars, 1000, 100_000);
        var preview = responseBody.Length > maxChars
            ? $"{responseBody[..maxChars]}...[truncated {responseBody.Length - maxChars} chars]"
            : responseBody;
        logger.LogInformation(
            "Etsy raw response for query {Query} (HTTP {StatusCode}): {ResponseBody}",
            query,
            (int)statusCode,
            preview);
    }

    private static CommerceMarketListing? Map(JsonElement item, string matchedQuery, int rank)
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
            matchedQuery,
            rank,
            item.GetRawText());
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

    private static string NormalizeQuery(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

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
