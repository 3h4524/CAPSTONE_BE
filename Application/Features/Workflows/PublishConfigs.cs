namespace APCS.Application.Features.Workflows;

/// <summary>
/// Settings of the Push to Printify node. The product's approved design is printed on the chosen colors and sizes of
/// one blueprint by one provider; each variant is priced to leave about <see cref="TargetProfit"/> after Printify's
/// cost and Etsy's fees.
/// </summary>
/// <param name="DesignId">The approved design to print; the final (or first approved) one when null.</param>
/// <param name="PublishToEtsy">False only creates the product on Printify, for the seller to publish there.</param>
/// <param name="AddMockups">Adds the approved APCS mock-ups and video to the Etsy listing Printify creates.</param>
/// <param name="LeadPhoto">
/// The photo the Etsy listing opens on: <c>printify</c> (Printify's photo of the garment it ships, the APCS mock-ups
/// after it) or <c>apcs</c> (the first APCS mock-up). Printify's when the product is set on Apply mock-up, else APCS.
/// </param>
/// <param name="Source">
/// <c>mockup</c>: the product is read from the approved mock-ups when they use system templates (named after the
/// Printify product, their colors mapped in <c>printify_color_map</c>), the provider chosen to make those colors.
/// <c>manual</c>: the product set on this node, for the seller's own templates and provider. Unset, a node with a
/// product keeps it and one without reads the mock-ups.
/// </param>
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
    bool? AddMockups,
    string? Position = null,
    string? LeadPhoto = null,
    string? Source = null)
{
    public const decimal DefaultProfit = 10m;

    /// <summary>Whether the product is read from the approved mock-ups (see <see cref="Source"/>).</summary>
    public bool FromMockups => Source is null ? BlueprintId is not > 0 : string.Equals(Source, "mockup", StringComparison.OrdinalIgnoreCase);

    /// <summary>These settings with the product chosen on Apply mock-up, which then decides what Printify makes.</summary>
    public PrintifyPublishConfig WithSetup(PrintifyProductSetup setup) => this with
    {
        BlueprintId = setup.BlueprintId, BlueprintTitle = setup.BlueprintTitle,
        PrintProviderId = setup.PrintProviderId, PrintProviderTitle = setup.PrintProviderTitle,
        Colors = setup.Colors, Sizes = setup.Sizes, Position = setup.Position,
    };

    /// <summary>Why the node's own settings (the profit) cannot be used, when Apply mock-up chose the product.</summary>
    public string? ProfitProblem() =>
        TargetProfit is < 0 or > 500 ? "Push to Printify: the profit per item must be between $0 and $500." : null;

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
/// <param name="MadeBy">For a physical item: <c>me</c> (the seller makes it) or <c>partner</c> (a production partner does).</param>
/// <param name="ProductionPartnerIds">The seller's Etsy production partners that make it, when <paramref name="MadeBy"/> is <c>partner</c>.</param>
public sealed record EtsyPublishConfig(
    string? ListingType,
    decimal? Price,
    int? Quantity,
    long? TaxonomyId,
    string? TaxonomyPath,
    string? WhoMade,
    string? WhenMade,
    long? ShippingProfileId,
    bool? IncludeVideo,
    string? MadeBy = null,
    IReadOnlyList<long>? ProductionPartnerIds = null)
{
    public string Type => ListingType == "physical" ? "physical" : "download";

    /// <summary>The production partners Etsy is told make the item: only for a physical item a partner makes.</summary>
    public IReadOnlyList<long> Partners => Type == "physical" && MadeBy == "partner" ? ProductionPartnerIds ?? [] : [];

    // Etsy requires a physical item to say who makes it, and to name the partner when one does: a listing that
    // hides its production partner breaks the Creativity Standards and can cost the seller the shop.
    public string? Problem() =>
        Price is not > 0 ? "Publish to Etsy: set a price."
        : TaxonomyId is not > 0 ? "Publish to Etsy: choose an Etsy category."
        : Type == "physical" && ShippingProfileId is not > 0 ? "Publish to Etsy: a physical listing needs a shipping profile."
        : Type == "physical" && MadeBy is not ("me" or "partner") ? "Publish to Etsy: say who makes this item, you or a production partner."
        : Type == "physical" && MadeBy == "partner" && ProductionPartnerIds is not { Count: > 0 } ? "Publish to Etsy: choose the production partner that makes this item."
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
    bool IncludeVideo = false,
    bool MockupsAfterPrintify = false,
    string? MadeAs = null,
    int SocialPosts = 0,
    IReadOnlyList<string>? Platforms = null);

/// <summary>
/// Push to media: the platforms to prepare posts for (<c>pinterest</c>, <c>tiktok</c>); none set means both. The
/// posts are made from the approved mock-ups, video and listing, for the seller to publish by hand.
/// </summary>
/// <param name="Pinterest">The canvas's Pinterest switch; unset counts as on.</param>
/// <param name="TikTok">The canvas's TikTok switch; unset counts as on.</param>
public sealed record PublishSocialConfig(IReadOnlyList<string>? Platforms = null, bool? Pinterest = null, bool? TikTok = null)
{
    public static readonly IReadOnlyList<string> AllPlatforms = ["pinterest", "tiktok"];

    /// <summary>A list of platforms wins; else the two switches; none chosen at all means both.</summary>
    public IReadOnlyList<string> Chosen()
    {
        if (Platforms?.Select(item => item.Trim().ToLowerInvariant()).Where(AllPlatforms.Contains).Distinct().ToArray() is { Length: > 0 } listed)
            return listed;
        string[] switched = [.. Pinterest != false ? ["pinterest"] : Array.Empty<string>(), .. TikTok != false ? ["tiktok"] : Array.Empty<string>()];
        return switched.Length > 0 ? switched : AllPlatforms;
    }
}
