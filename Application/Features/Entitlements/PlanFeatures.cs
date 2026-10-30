namespace APCS.Application.Features.Entitlements;

/// <summary>
/// The <c>plan_features.feature_code</c> values that gate a Seller's access to a feature.
/// </summary>
/// <remarks>
/// Plans differ by feature, not by monthly usage quotas: a flag turns a feature on for a plan, and
/// a limit sets how far it goes (variations per product, a daily safety cap). Batch size,
/// concurrent jobs and priority support keep their own <c>subscription_plans</c> columns.
/// </remarks>
public static class PlanFeatures
{
    public const string DesignVariations = "design_variations";
    public const string AutoApproval = "auto_approval";
    public const string CustomMockupTemplates = "custom_mockup_templates";
    public const string LifestyleMockup = "lifestyle_mockup";
    public const string ListingRegenerate = "listing_regenerate";
    public const string AllVideoFormats = "all_video_formats";
    public const string AiVideo = "ai_video";
    public const string StudioKeyframe = "studio_keyframe";
    public const string DailyMockupCap = "daily_mockup_cap";
    public const string DailyListingCap = "daily_listing_cap";
    public const string DailyVideoCap = "daily_video_cap";
    public const string WorkflowListing = "workflow_listing";
    public const string WorkflowPublish = "workflow_publish";

    /// <summary>Every feature an Admin can configure on a plan, in display order.</summary>
    public static readonly IReadOnlyList<PlanFeatureDefinition> All =
    [
        new(DesignVariations, "Design variations per product", PlanFeatureKinds.Limit, "Design images",
            "How many design variations a Seller can generate for one product."),
        new(AutoApproval, "Automatic design approval", PlanFeatureKinds.Flag, "Design images",
            "Approve generated designs automatically instead of reviewing each one."),
        new(CustomMockupTemplates, "Custom mockup templates", PlanFeatureKinds.Flag, "Mockups",
            "Create and edit the Seller's own mockup templates and garment masks."),
        new(LifestyleMockup, "Lifestyle mockups (AI)", PlanFeatureKinds.Flag, "Mockups",
            "Generate lifestyle mockups with the Seller's own Gemini key."),
        new(ListingRegenerate, "Listing regeneration and SEO score", PlanFeatureKinds.Flag, "Listing",
            "Regenerate a listing and score its SEO."),
        new(AllVideoFormats, "Landscape video", PlanFeatureKinds.Flag, "Video",
            "Render standard videos in landscape as well as square and portrait."),
        new(AiVideo, "AI video (Veo)", PlanFeatureKinds.Flag, "Video",
            "Build videos from AI scenes, billed to the Seller's own Google key."),
        new(StudioKeyframe, "AI studio keyframes", PlanFeatureKinds.Flag, "Video",
            "Generate AI studio start frames for AI video scenes."),
        new(DailyMockupCap, "Daily mockup safety cap", PlanFeatureKinds.Limit, "Safety caps",
            "Most mockups rendered per day. Leave empty for no cap."),
        new(DailyListingCap, "Daily listing safety cap", PlanFeatureKinds.Limit, "Safety caps",
            "Most listings generated per day. Leave empty for no cap."),
        new(DailyVideoCap, "Daily video safety cap", PlanFeatureKinds.Limit, "Safety caps",
            "Most standard videos rendered per day. Leave empty for no cap."),
        new(WorkflowListing, "Listing content", PlanFeatureKinds.Flag, "Workflow",
            "Write listing titles, tags and descriptions, in a workflow or on their own."),
        new(WorkflowPublish, "Publish to Etsy and Printify", PlanFeatureKinds.Flag, "Workflow",
            "Publish finished products to Etsy and Printify, once those steps are available."),
    ];

    private static readonly HashSet<string> Codes = All.Select(feature => feature.Code).ToHashSet(StringComparer.Ordinal);

    /// <summary>Checks whether a code is one of <see cref="All"/>.</summary>
    public static bool IsKnown(string code) => Codes.Contains(code);

    /// <summary>Finds the definition of a code, or null when it is not one of <see cref="All"/>.</summary>
    public static PlanFeatureDefinition? Find(string code) => All.FirstOrDefault(feature => feature.Code == code);
}

/// <summary>Whether a plan feature is switched on and off, or carries a number.</summary>
public static class PlanFeatureKinds
{
    public const string Flag = "flag";
    public const string Limit = "limit";
}

/// <summary>Describes one configurable plan feature.</summary>
public sealed record PlanFeatureDefinition(string Code, string Label, string Kind, string Group, string Description);
