namespace APCS.Application.Features.Shops.Dtos.Request;

/// <summary>What the seller confirmed in the push form; sent to Etsy as one draft listing.</summary>
/// <param name="Type"><c>physical</c> or <c>download</c>.</param>
/// <param name="MockupIds">Approved mock-ups of the product, in the order Etsy should show them.</param>
/// <param name="DesignIds">For a <c>download</c> listing: the design files the buyer receives after paying.</param>
public sealed record PublishEtsyDraftRequestDto(
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    decimal Price,
    int Quantity,
    long TaxonomyId,
    string? TaxonomyPath,
    string Type,
    string WhoMade,
    string WhenMade,
    long? ShippingProfileId,
    long? ReadinessStateId,
    IReadOnlyList<long> ProductionPartnerIds,
    IReadOnlyList<Guid> MockupIds,
    bool IncludeVideo,
    IReadOnlyList<Guid>? DesignIds = null);
