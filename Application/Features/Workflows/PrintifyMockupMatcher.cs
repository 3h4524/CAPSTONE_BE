using System.Text.RegularExpressions;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Shops;
using APCS.Application.Features.Shops.Dtos.Response;
using APCS.Common.Models;
using APCS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace APCS.Application.Features.Workflows;

/// <summary>What Printify makes for a product, read from its approved mock-ups, and what the seller should know.</summary>
/// <param name="TemplateName">The system template the mock-ups show, whose name is the Printify product's.</param>
public sealed record PrintifyMockupMatch(PrintifyProductSetup Setup, string TemplateName, IReadOnlyList<string> Warnings)
{
    /// <summary>One line for the run panel: what is made, by whom, in which colors and sizes.</summary>
    public string Summary =>
        $"{Setup.BlueprintTitle} by {Setup.PrintProviderTitle} in {string.Join(", ", Setup.Colors ?? [])} ({string.Join(", ", Setup.Sizes ?? [])})";
}

/// <summary>Reads the Printify product of a product from the mock-ups the seller approved.</summary>
public interface IPrintifyMockupMatcher
{
    /// <summary>
    /// The Printify product the approved mock-ups show, or null when none of them uses a system template: the seller's
    /// own templates are made by their own provider, chosen on the node.
    /// </summary>
    /// <param name="config">The node's settings: a provider or sizes the seller chose there are kept.</param>
    Task<Result<PrintifyMockupMatch?>> MatchAsync(Guid productId, PrintifyPublishConfig config, CancellationToken cancellationToken);
}

/// <summary>
/// A system mock-up template is named after the Printify product (blueprint) its photo shows, and each garment color of
/// the mock-ups has a Printify color name in <c>printify_color_map</c>. The blueprint is found by that name (an admin
/// link of the template wins), the colors through that map, and the provider is the one that makes the most of those
/// colors, a US one first, then the cheapest US shipping. Mock-ups of a garment's other sides (back, sleeves) are left out:
/// they belong to their front's template.
/// </summary>
public sealed class PrintifyMockupMatcher(
    IRepository<MockupImage> mockups,
    IMockupTemplatePrintifyLinkRepository links,
    IPrintifyColorMapRepository colorMap,
    IPrintifyService printify) : IPrintifyMockupMatcher
{
    /// <summary>Sizes made when neither the node nor the template's link names any.</summary>
    internal static readonly string[] DefaultSizes = ["S", "M", "L", "XL", "2XL", "3XL"];

    /// <summary>Providers whose catalog is read to choose one; the list comes US first.</summary>
    internal const int ProvidersCompared = 8;

    public async Task<Result<PrintifyMockupMatch?>> MatchAsync(Guid productId, PrintifyPublishConfig config, CancellationToken cancellationToken)
    {
        var approved = await mockups.Query()
            .Include(mockup => mockup.MockupTemplate)
            .Where(mockup => mockup.ProductId == productId && mockup.DeletedAt == null && mockup.ApprovalStatus == "approved"
                && mockup.ApprovedRevision == mockup.MetadataRevision && mockup.MockupTemplate != null && mockup.MockupTemplate.UserId == null)
            .OrderBy(mockup => mockup.CreatedAt)
            .ToListAsync(cancellationToken);
        // A back or sleeve photo is a template of its own, kept with its garment's front: the front says which garment
        // and colors Printify makes, and the sides are printed on it as extra print areas.
        approved = approved.Where(mockup => !BatchMockups.Common.MockupRules.IsOtherView(mockup.MockupTemplate!)).ToList();
        if (approved.Count == 0)
            return Result.Success<PrintifyMockupMatch?>(null);

        var warnings = new List<string>();
        // One listing is one Printify product: the template most mock-ups show, the first one used on a tie.
        var template = approved.GroupBy(mockup => mockup.MockupTemplateId)
            .OrderByDescending(group => group.Count())
            .First().First().MockupTemplate!;
        if (approved.Select(mockup => mockup.MockupTemplateId).Distinct().Count() > 1)
            warnings.Add($"The mock-ups show several products; Printify makes {template.Name}, the one most of them show.");

        var link = (await links.GetAsync([template.Id], cancellationToken)).GetValueOrDefault(template.Id);
        long blueprintId;
        string blueprintTitle;
        if (link is not null)
            (blueprintId, blueprintTitle) = (link.BlueprintId, link.BlueprintTitle);
        else
        {
            var found = await printify.SearchBlueprintsAsync(template.Name, cancellationToken);
            if (found.IsFailure)
                return Result.Failure<PrintifyMockupMatch?>(found.Error);
            var key = Key(template.Name);
            if (found.Value.FirstOrDefault(item => Key(item.Title) == key || Key($"{item.Brand} {item.Model}") == key) is not { } blueprint)
                return Result.Failure<PrintifyMockupMatch?>(Error.Validation(
                    $"No Printify product is named \"{template.Name}\", the template the mock-ups use. Name the template after its Printify product, or link it on the admin templates page."));
            (blueprintId, blueprintTitle) = (blueprint.Id, blueprint.Title);
        }

        // A mock-up left in its photo's own color names none: the template's color is that photo's.
        var hexes = approved.Where(mockup => mockup.MockupTemplateId == template.Id)
            .Select(mockup => mockup.GarmentColor ?? template.GarmentColor)
            .OfType<string>()
            .Select(hex => hex.Trim().ToUpperInvariant())
            .Distinct()
            .ToArray();
        var names = await colorMap.NamesAsync(blueprintId, hexes, cancellationToken);
        var unmapped = hexes.Where(hex => !names.ContainsKey(hex)).ToArray();
        if (unmapped.Length > 0)
            warnings.Add($"No Printify color is set for {string.Join(", ", unmapped)}; left out.");
        var wanted = hexes.Where(names.ContainsKey).Select(hex => names[hex]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (wanted.Length == 0)
            return Result.Failure<PrintifyMockupMatch?>(Error.Validation(hexes.Length == 0
                ? "The mock-ups name no garment color, so Push to Printify cannot tell which colors to make."
                : $"None of the mock-up colors ({string.Join(", ", hexes)}) has a Printify color yet, so Push to Printify cannot tell which colors to make."));

        var providers = await printify.GetProvidersAsync(blueprintId, cancellationToken);
        if (providers.IsFailure)
            return Result.Failure<PrintifyMockupMatch?>(providers.Error);
        var chosen = config.PrintProviderId ?? link?.PrintProviderId;
        var candidates = providers.Value
            .OrderByDescending(provider => provider.Id == chosen)
            .Take(ProvidersCompared)
            .ToArray();
        if (candidates.Length == 0)
            return Result.Failure<PrintifyMockupMatch?>(Error.Validation($"No print provider makes {blueprintTitle} on Printify right now."));

        var offers = new List<Offer>();
        for (var order = 0; order < candidates.Length; order++)
        {
            var provider = candidates[order];
            var catalog = await printify.GetCatalogAsync(blueprintId, provider.Id, cancellationToken);
            if (catalog.IsFailure)
                continue;
            var colors = wanted
                .Select(name => catalog.Value.Colors.FirstOrDefault(color => string.Equals(color, name, StringComparison.OrdinalIgnoreCase)))
                .OfType<string>()
                .ToArray();
            offers.Add(new Offer(provider, catalog.Value, colors, order));
        }
        if (offers.Count == 0)
            return Result.Failure<PrintifyMockupMatch?>(Error.Failure("Printify.CatalogUnavailable", "Printify's catalog could not be read. Retry this step in a moment."));

        // The seller's provider is kept while it makes any of the colors; otherwise the best maker of them.
        var offer = offers.FirstOrDefault(item => item.Provider.Id == chosen && item.Colors.Length > 0)
            ?? offers.OrderByDescending(item => item.Colors.Length)
                .ThenByDescending(item => IsUnitedStates(item.Provider.Country))
                .ThenBy(item => item.Catalog.UsShippingFirstItem ?? decimal.MaxValue)
                .ThenBy(item => item.Order)
                .First();
        if (offer.Colors.Length == 0)
            return Result.Failure<PrintifyMockupMatch?>(Error.Validation(
                $"No print provider makes {blueprintTitle} in {string.Join(", ", wanted)}. Check the Printify color names in printify_color_map."));
        if (chosen is { } kept && offer.Provider.Id != kept)
            warnings.Add($"The chosen provider does not make these colors of {blueprintTitle}; {offer.Provider.Title} is used.");
        var missing = wanted.Where(name => !offer.Colors.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (missing.Length > 0)
            warnings.Add($"{offer.Provider.Title} does not make {string.Join(", ", missing)}; left out.");

        IReadOnlyList<string> wantedSizes = config.Sizes is { Count: > 0 } ? config.Sizes
            : link?.DefaultSizes is { Count: > 0 } ? link.DefaultSizes
            : DefaultSizes;
        var sizes = offer.Catalog.Sizes.Where(size => wantedSizes.Contains(size, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (sizes.Length == 0)
            sizes = offer.Catalog.Sizes.ToArray();

        var setup = new PrintifyProductSetup(blueprintId, blueprintTitle, offer.Provider.Id, offer.Provider.Title, offer.Colors, sizes,
            config.Position ?? "front");
        return Result.Success<PrintifyMockupMatch?>(new PrintifyMockupMatch(setup, template.Name, warnings));
    }

    private sealed record Offer(PrintifyProviderDto Provider, PrintifyCatalogDto Catalog, string[] Colors, int Order);

    /// <summary>A name compared without case, punctuation or extra spaces: "Bella+Canvas 3001" is "bella canvas 3001".</summary>
    internal static string Key(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();

    private static bool IsUnitedStates(string? country) =>
        country is not null && (country.Equals("US", StringComparison.OrdinalIgnoreCase) || country.Equals("USA", StringComparison.OrdinalIgnoreCase)
            || country.Contains("United States", StringComparison.OrdinalIgnoreCase));
}
