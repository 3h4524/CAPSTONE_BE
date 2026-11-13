using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using APCS.Application.Abstractions.Commerce;
using Microsoft.Extensions.Logging;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Printify API v1 adapter. The seller's token rides on each request and never reaches a log; failures surface as
/// <see cref="PrintifyApiException"/> carrying Printify's reason, not the response body.
/// </summary>
public sealed class PrintifyClient(
    IHttpClientFactory clients,
    ILogger<PrintifyClient> logger) : IPrintifyClient
{
    public const string HttpClientName = "Printify";

    public async Task<IReadOnlyList<PrintifyShop>> GetShopsAsync(string token, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, "shops.json", token, null, cancellationToken);
        return Items(document.RootElement)
            .Select(shop => new PrintifyShop(Number(shop, "id") ?? 0, Text(shop, "title") ?? string.Empty, Text(shop, "sales_channel") ?? "disconnected"))
            .Where(shop => shop.Id > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<PrintifyBlueprint>> GetBlueprintsAsync(string token, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, "catalog/blueprints.json", token, null, cancellationToken);
        return Items(document.RootElement)
            .Select(blueprint => new PrintifyBlueprint(
                Number(blueprint, "id") ?? 0,
                Text(blueprint, "title") ?? string.Empty,
                Text(blueprint, "brand") ?? string.Empty,
                Text(blueprint, "model") ?? string.Empty,
                blueprint.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array
                    ? images.EnumerateArray().Where(image => image.ValueKind == JsonValueKind.String).Select(image => image.GetString()).FirstOrDefault()
                    : null))
            .Where(blueprint => blueprint.Id > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<PrintifyProvider>> GetProvidersAsync(string token, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, "catalog/print_providers.json", token, null, cancellationToken);
        return Items(document.RootElement).Select(ReadProvider).Where(provider => provider.Id > 0).ToArray();
    }

    public async Task<IReadOnlyList<PrintifyProvider>> GetBlueprintProvidersAsync(string token, long blueprintId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, $"catalog/blueprints/{blueprintId}/print_providers.json", token, null, cancellationToken);
        return Items(document.RootElement).Select(ReadProvider).Where(provider => provider.Id > 0).ToArray();
    }

    public async Task<IReadOnlyList<PrintifyCatalogVariant>> GetVariantsAsync(string token, long blueprintId, long providerId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, $"catalog/blueprints/{blueprintId}/print_providers/{providerId}/variants.json", token, null, cancellationToken);
        var root = document.RootElement;
        var variants = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("variants", out var list) ? Items(list) : Items(root);
        return variants.Select(variant =>
        {
            var options = variant.TryGetProperty("options", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
            var placeholders = variant.TryGetProperty("placeholders", out var areas) && areas.ValueKind == JsonValueKind.Array
                ? areas.EnumerateArray().Select(area => new PrintifyPlaceholder(
                    Text(area, "position") ?? "front", (int)(Number(area, "width") ?? 0), (int)(Number(area, "height") ?? 0))).ToArray()
                : [];
            return new PrintifyCatalogVariant(
                Number(variant, "id") ?? 0,
                Text(variant, "title") ?? string.Empty,
                options.ValueKind == JsonValueKind.Object ? Text(options, "color") : null,
                options.ValueKind == JsonValueKind.Object ? Text(options, "size") : null,
                placeholders);
        }).Where(variant => variant.Id > 0).ToArray();
    }

    public async Task<IReadOnlyList<PrintifyShippingRate>> GetShippingAsync(string token, long blueprintId, long providerId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, $"catalog/blueprints/{blueprintId}/print_providers/{providerId}/shipping.json", token, null, cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("profiles", out var profiles))
            return [];
        return Items(profiles).Select(profile => new PrintifyShippingRate(
            profile.TryGetProperty("variant_ids", out var ids) && ids.ValueKind == JsonValueKind.Array
                ? ids.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.Number).Select(id => id.GetInt64()).ToArray()
                : [],
            profile.TryGetProperty("countries", out var countries) && countries.ValueKind == JsonValueKind.Array
                ? countries.EnumerateArray().Where(country => country.ValueKind == JsonValueKind.String).Select(country => country.GetString()!).ToArray()
                : [],
            Cost(profile, "first_item"),
            Cost(profile, "additional_items"),
            profile.TryGetProperty("first_item", out var first) && first.ValueKind == JsonValueKind.Object ? Text(first, "currency") ?? "USD" : "USD")).ToArray();
    }

    public async Task<PrintifyUploadedImage> UploadImageAsync(string token, string fileName, string sourceUrl, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Post, "uploads/images.json", token, new { file_name = fileName, url = sourceUrl }, cancellationToken);
        var root = document.RootElement;
        var id = Text(root, "id");
        if (string.IsNullOrEmpty(id))
            throw new PrintifyApiException(PrintifyFailureKind.Unavailable, "Printify returned no image id.");
        return new PrintifyUploadedImage(id, (int)(Number(root, "width") ?? 0), (int)(Number(root, "height") ?? 0));
    }

    public async Task<PrintifyProduct> CreateProductAsync(string token, long shopId, PrintifyNewProduct product, CancellationToken cancellationToken)
    {
        var body = new
        {
            title = product.Title,
            description = product.Description,
            tags = product.Tags,
            blueprint_id = product.BlueprintId,
            print_provider_id = product.PrintProviderId,
            variants = product.Variants.Select(variant => new { id = variant.Id, price = variant.PriceCents, is_enabled = variant.IsEnabled }),
            print_areas = new[]
            {
                new
                {
                    variant_ids = product.Variants.Select(variant => variant.Id),
                    // The main print area first, then the same design on each further area (the back, a sleeve).
                    placeholders = new[] { new PrintifyPlacement(product.Position, product.Scale, product.X, product.Y) }
                        .Concat(product.MorePlacements ?? [])
                        .Select(placement => new
                        {
                            position = placement.Position,
                            images = new[] { new { id = product.ImageId, x = placement.X, y = placement.Y, scale = placement.Scale, angle = 0 } },
                        }),
                },
            },
        };
        using var document = await SendAsync(HttpMethod.Post, $"shops/{shopId}/products.json", token, body, cancellationToken);
        return ReadProduct(document.RootElement);
    }

    public async Task<PrintifyProduct> GetProductAsync(string token, long shopId, string productId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, $"shops/{shopId}/products/{Uri.EscapeDataString(productId)}.json", token, null, cancellationToken);
        return ReadProduct(document.RootElement);
    }

    public async Task<PrintifyProduct> UpdatePricesAsync(string token, long shopId, string productId, IReadOnlyList<PrintifyVariantPrice> variants, CancellationToken cancellationToken)
    {
        var body = new { variants = variants.Select(variant => new { id = variant.Id, price = variant.PriceCents, is_enabled = variant.IsEnabled }) };
        using var document = await SendAsync(HttpMethod.Put, $"shops/{shopId}/products/{Uri.EscapeDataString(productId)}.json", token, body, cancellationToken);
        return ReadProduct(document.RootElement);
    }

    public async Task PublishAsync(string token, long shopId, string productId, CancellationToken cancellationToken)
    {
        // Everything APCS set goes to the listing; Printify also brings its shipping template so orders route back to it.
        var body = new { title = true, description = true, images = true, variants = true, tags = true, keyFeatures = true, shipping_template = true };
        using var _ = await SendAsync(HttpMethod.Post, $"shops/{shopId}/products/{Uri.EscapeDataString(productId)}/publish.json", token, body, cancellationToken);
    }

    internal static PrintifyProduct ReadProduct(JsonElement root)
    {
        string? externalId = null, handle = null;
        if (root.TryGetProperty("external", out var external) && external.ValueKind == JsonValueKind.Object)
        {
            externalId = Text(external, "id");
            handle = Text(external, "handle");
        }
        var variants = root.TryGetProperty("variants", out var list)
            ? Items(list).Select(variant => new PrintifyProductVariant(
                Number(variant, "id") ?? 0,
                Text(variant, "title") ?? string.Empty,
                Number(variant, "cost") ?? 0,
                Number(variant, "price") ?? 0,
                Flag(variant, "is_enabled"),
                !variant.TryGetProperty("is_available", out var available) || available.ValueKind != JsonValueKind.False)).ToArray()
            : [];
        var images = root.TryGetProperty("images", out var pictures)
            ? Items(pictures).Select(image => Text(image, "src")).Where(src => !string.IsNullOrEmpty(src)).Select(src => src!).ToArray()
            : [];
        return new PrintifyProduct(
            Text(root, "id") ?? string.Empty,
            Text(root, "title") ?? string.Empty,
            Flag(root, "is_locked"),
            string.IsNullOrWhiteSpace(externalId) ? null : externalId,
            string.IsNullOrWhiteSpace(handle) ? null : handle,
            variants,
            images);
    }

    private static PrintifyProvider ReadProvider(JsonElement provider)
    {
        var location = provider.TryGetProperty("location", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
        return new PrintifyProvider(
            Number(provider, "id") ?? 0,
            Text(provider, "title") ?? string.Empty,
            location.ValueKind == JsonValueKind.Object ? Text(location, "country") : null,
            location.ValueKind == JsonValueKind.Object ? Text(location, "region") : null);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string token, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await clients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new PrintifyApiException(PrintifyFailureKind.Unavailable, "Printify could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PrintifyApiException(PrintifyFailureKind.Unavailable, "Printify did not answer in time.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var kind = Classify(response.StatusCode);
                var reason = Reason(text);
                logger.LogWarning("Printify {Method} {Path} returned {Status} ({Kind}).", method, path, (int)response.StatusCode, kind);
                throw new PrintifyApiException(kind, $"Printify returned {(int)response.StatusCode}.", reason);
            }
            try
            {
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            }
            catch (JsonException)
            {
                throw new PrintifyApiException(PrintifyFailureKind.Unavailable, "Printify returned an unreadable answer.");
            }
        }
    }

    internal static PrintifyFailureKind Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => PrintifyFailureKind.Unauthorized,
        HttpStatusCode.TooManyRequests => PrintifyFailureKind.RateLimited,
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound => PrintifyFailureKind.Rejected,
        _ => PrintifyFailureKind.Unavailable,
    };

    /// <summary>
    /// Printify errors look like <c>{ "message": "Validation failed.", "errors": { "reason": "Image has low quality" } }</c>;
    /// the most specific text wins, cut short so a long payload never reaches the seller.
    /// </summary>
    internal static string? Reason(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            string? reason = null;
            if (root.TryGetProperty("errors", out var errors))
                reason = errors.ValueKind switch
                {
                    JsonValueKind.Object when Text(errors, "reason") is { } text => text,
                    JsonValueKind.Object => errors.EnumerateObject().Select(property => FirstText(property.Value)).FirstOrDefault(text => text is not null),
                    JsonValueKind.Array => errors.EnumerateArray().Select(FirstText).FirstOrDefault(text => text is not null),
                    JsonValueKind.String => errors.GetString(),
                    _ => null,
                };
            reason ??= Text(root, "message") ?? Text(root, "error");
            return reason is { Length: > 300 } ? reason[..300] : reason;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Array => element.EnumerateArray().Select(FirstText).FirstOrDefault(text => text is not null),
        JsonValueKind.Object => Text(element, "reason") ?? Text(element, "message"),
        _ => null,
    };

    private static long Cost(JsonElement profile, string name) =>
        profile.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? Number(value, "cost") ?? 0 : 0;

    private static IEnumerable<JsonElement> Items(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray(),
        // Some list endpoints page their answer as { data: [...] }.
        JsonValueKind.Object when element.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array => data.EnumerateArray(),
        _ => [],
    };

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static long? Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetInt64(out var whole) ? whole : (long)Math.Round(value.GetDouble());
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
