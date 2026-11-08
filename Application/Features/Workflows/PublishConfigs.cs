namespace APCS.Application.Features.Workflows;

/// <summary>
/// Settings of the Push to Printify node. The product's approved design is printed on the chosen colors and sizes of
/// one blueprint by one provider; each variant is priced to leave about <see cref="TargetProfit"/> after Printify's
/// cost and Etsy's fees.
/// </summary>
/// <param name="DesignId">The approved design to print; the final (or first approved) one when null.</param>
/// <param name="PublishToEtsy">False only creates the product on Printify, for the seller to publish there.</param>
/// <param name="AddMockups">Adds the approved APCS mock-ups and video to the Etsy listing Printify creates.</param>
public sealed record PrintifyPublishConfig(
    long? BlueprintId,
    string? BlueprintTitle,
    long? PrintProviderId,
    string? PrintProviderTitle,
    IReadOnlyList<string>? Colors,
    IReadOnlyList<string>? Sizes,
    decimal? TargetProfit,
    Guid? DesignId,
    bool? PublishToEtsy,
    bool? AddMockups)
{
    public const decimal DefaultProfit = 10m;

    /// <summary>Why the node cannot run with these settings, or null.</summary>
    public string? Problem() =>
        BlueprintId is not > 0 ? "Push to Printify: choose a product type."
        : PrintProviderId is not > 0 ? "Push to Printify: choose a print provider."
        : Colors is not { Count: > 0 } || Sizes is not { Count: > 0 } ? "Push to Printify: choose at least one color and one size."
        : TargetProfit is < 0 or > 500 ? "Push to Printify: the profit per item must be between $0 and $500."
        : null;
}

/// <summary>Settings of the Publish to Etsy node: one draft listing from the product's content and approved media.</summary>
/// <param name="ListingType"><c>download</c> (the approved designs are the files buyers get) or <c>physical</c>.</param>
public sealed record EtsyPublishConfig(
    string? ListingType,
    decimal? Price,
    int? Quantity,
    long? TaxonomyId,
    string? TaxonomyPath,
    string? WhoMade,
    string? WhenMade,
    long? ShippingProfileId,
    bool? IncludeVideo)
{
    public string Type => ListingType == "physical" ? "physical" : "download";

    public string? Problem() =>
        Price is not > 0 ? "Publish to Etsy: set a price."
        : TaxonomyId is not > 0 ? "Publish to Etsy: choose an Etsy category."
        : Type == "physical" && ShippingProfileId is not > 0 ? "Publish to Etsy: a physical listing needs a shipping profile."
        : null;
}

/// <summary>Where a publish node is, kept as its output snapshot so a restart carries on instead of starting over.</summary>
/// <param name="Stage"><c>queued</c>, <c>publishing</c> (Printify is publishing to Etsy) or <c>done</c>.</param>
public sealed record PublishNodeState(
    string Stage,
    string? PrintifyProductId = null,
    DateTime? PublishRequestedAt = null,
    string? EtsyListingId = null,
    string? EtsyUrl = null,
    int MediaAttached = 0,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<Guid>? MockupIds = null,
    bool IncludeVideo = false);
