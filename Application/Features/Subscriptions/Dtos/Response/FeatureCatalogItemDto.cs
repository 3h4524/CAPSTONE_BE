namespace APCS.Application.Features.Subscriptions.Dtos.Response;

/// <summary>
/// One plan feature as the catalog shows it, so the client lists plans with the Admin's labels.
/// </summary>
/// <param name="Kind">"flag" or "limit".</param>
/// <param name="Unit">For a limit, what it counts, such as "per day"; otherwise null.</param>
public sealed record FeatureCatalogItemDto(
    string Code,
    string Label,
    string Description,
    string Group,
    string Kind,
    string? Unit,
    int SortOrder);
