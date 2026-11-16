using APCS.Application.Abstractions.Caching;
using APCS.Application.Abstractions.Commerce;
using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Common.Models;
using Microsoft.Extensions.Logging;

namespace APCS.Application.Features.BatchMockups.Printify;

/// <summary>The Printify catalog as admins read it, with the server's own token.</summary>
public interface IPrintifyAdminCatalog
{
    Task<Result<IReadOnlyList<PrintifyBlueprintDto>>> SearchBlueprintsAsync(string? query, CancellationToken cancellationToken = default);

    /// <summary>The providers that make a blueprint, those in the United States first.</summary>
    Task<Result<IReadOnlyList<PrintifyProviderDto>>> GetProvidersAsync(long blueprintId, CancellationToken cancellationToken = default);

    /// <summary>What a provider makes of a blueprint: its colors, sizes and print areas.</summary>
    Task<Result<PrintifyCatalogDto>> GetCatalogAsync(long blueprintId, long providerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the catalog with <see cref="IPrintifyAdminToken"/>, never a seller's key, and keeps it a day under the same
/// keys the sellers' Printify feature uses (the catalog is the same for every account).
/// </summary>
public sealed class PrintifyAdminCatalog(
    IPrintifyClient printify,
    IPrintifyAdminToken adminToken,
    ICacheService cache,
    ILogger<PrintifyAdminCatalog> logger) : IPrintifyAdminCatalog
{
    private static readonly TimeSpan CatalogLifetime = TimeSpan.FromDays(1);

    public async Task<Result<IReadOnlyList<PrintifyBlueprintDto>>> SearchBlueprintsAsync(string? query, CancellationToken cancellationToken = default)
    {
        if (adminToken.Token is not { } token)
            return Result.Failure<IReadOnlyList<PrintifyBlueprintDto>>(PrintifyTemplateErrors.NoToken());
        try
        {
            return Result.Success(PrintifyService.RankBlueprints(await BlueprintsAsync(token, cancellationToken), query));
        }
        catch (PrintifyApiException exception)
        {
            return Result.Failure<IReadOnlyList<PrintifyBlueprintDto>>(Failure(exception));
        }
    }

    public async Task<Result<IReadOnlyList<PrintifyProviderDto>>> GetProvidersAsync(long blueprintId, CancellationToken cancellationToken = default)
    {
        if (adminToken.Token is not { } token)
            return Result.Failure<IReadOnlyList<PrintifyProviderDto>>(PrintifyTemplateErrors.NoToken());
        try
        {
            return Result.Success<IReadOnlyList<PrintifyProviderDto>>((await ProvidersAsync(token, blueprintId, cancellationToken))
                .Select(provider => new PrintifyProviderDto(provider.Id, provider.Title, provider.Country, provider.Region))
                .ToArray());
        }
        catch (PrintifyApiException exception)
        {
            return Result.Failure<IReadOnlyList<PrintifyProviderDto>>(Failure(exception));
        }
    }

    public async Task<Result<PrintifyCatalogDto>> GetCatalogAsync(long blueprintId, long providerId, CancellationToken cancellationToken = default)
    {
        if (adminToken.Token is not { } token)
            return Result.Failure<PrintifyCatalogDto>(PrintifyTemplateErrors.NoToken());
        try
        {
            var variants = await VariantsAsync(token, blueprintId, providerId, cancellationToken);
            return Result.Success(new PrintifyCatalogDto(
                blueprintId, providerId,
                variants.Select(variant => new PrintifyVariantOptionDto(variant.Id, variant.Title, variant.Color, variant.Size)).ToArray(),
                variants.Select(variant => variant.Color).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                variants.Select(variant => variant.Size).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                variants.SelectMany(variant => variant.Placeholders).Select(area => area.Position).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                null, null));
        }
        catch (PrintifyApiException exception)
        {
            return Result.Failure<PrintifyCatalogDto>(Failure(exception));
        }
    }

    internal Task<IReadOnlyList<PrintifyBlueprint>> BlueprintsAsync(string token, CancellationToken cancellationToken) =>
        CachedAsync("printify:blueprints:v1", () => printify.GetBlueprintsAsync(token, cancellationToken), cancellationToken);

    internal async Task<IReadOnlyList<PrintifyProvider>> ProvidersAsync(string token, long blueprintId, CancellationToken cancellationToken)
    {
        var makers = await CachedAsync($"printify:blueprint:{blueprintId}:providers:v1",
            () => printify.GetBlueprintProvidersAsync(token, blueprintId, cancellationToken), cancellationToken);
        var locations = (await CachedAsync("printify:providers:v1", () => printify.GetProvidersAsync(token, cancellationToken), cancellationToken))
            .ToDictionary(provider => provider.Id);
        return makers
            .Select(maker => locations.TryGetValue(maker.Id, out var known) ? known with { Title = maker.Title.Length > 0 ? maker.Title : known.Title } : maker)
            // Buyers are mostly in the United States: a provider there ships fastest and cheapest.
            .OrderByDescending(provider => IsUnitedStates(provider.Country))
            .ThenBy(provider => provider.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal Task<IReadOnlyList<PrintifyCatalogVariant>> VariantsAsync(string token, long blueprintId, long providerId, CancellationToken cancellationToken) =>
        CachedAsync($"printify:catalog:{blueprintId}:{providerId}:variants:v1",
            () => printify.GetVariantsAsync(token, blueprintId, providerId, cancellationToken), cancellationToken);

    /// <summary>
    /// The color of every variant id of a blueprint, over all its providers: a mock-up photo may name a variant of
    /// another provider with the same color.
    /// </summary>
    internal async Task<IReadOnlyDictionary<long, string>> ColorsOfAllProvidersAsync(string token, long blueprintId, CancellationToken cancellationToken)
    {
        var colors = new Dictionary<long, string>();
        foreach (var provider in await ProvidersAsync(token, blueprintId, cancellationToken))
        {
            try
            {
                foreach (var variant in await VariantsAsync(token, blueprintId, provider.Id, cancellationToken))
                    if (variant.Color is { } color)
                        colors.TryAdd(variant.Id, color);
            }
            catch (PrintifyApiException exception)
            {
                logger.LogInformation("Printify variants of blueprint {BlueprintId} by provider {ProviderId} could not be read: {Kind}.",
                    blueprintId, provider.Id, exception.Kind);
            }
        }
        return colors;
    }

    internal static Error Failure(PrintifyApiException exception) =>
        exception.Kind == PrintifyFailureKind.Unauthorized
            ? Error.Conflict("PrintifyTemplates.TokenRefused", "Printify refused Printify__CatalogToken. Put a valid token on the server.")
            : PrintifyTemplateErrors.Printify(exception.Reason);

    private async Task<IReadOnlyList<T>> CachedAsync<T>(string key, Func<Task<IReadOnlyList<T>>> load, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<T[]>(key, cancellationToken);
        if (cached is not null)
            return cached;
        var loaded = (await load()).ToArray();
        await cache.SetAsync(key, loaded, CatalogLifetime, cancellationToken);
        return loaded;
    }

    private static bool IsUnitedStates(string? country) =>
        country is not null && (country.Equals("US", StringComparison.OrdinalIgnoreCase) || country.Equals("United States", StringComparison.OrdinalIgnoreCase)
            || country.Equals("USA", StringComparison.OrdinalIgnoreCase));
}
