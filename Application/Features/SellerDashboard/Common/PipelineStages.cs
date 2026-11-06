namespace APCS.Application.Features.SellerDashboard.Common;

/// <summary>
/// Folds the fifteen <c>products.processing_status</c> values into the Dashboard's pipeline columns.
/// </summary>
public static class PipelineStages
{
    public const string Queued = "queued";
    public const string Designing = "designing";
    public const string DesignReview = "design_review";
    public const string Mockup = "mockup";
    public const string Video = "video";
    public const string Listing = "listing";
    public const string Ready = "ready";
    public const string Published = "published";

    /// <summary>The columns in pipeline order.</summary>
    public static readonly IReadOnlyList<string> Ordered =
        [Queued, Designing, DesignReview, Mockup, Video, Listing, Ready, Published];

    /// <summary>The statuses in which a product is being processed by a worker rather than waiting.</summary>
    public static readonly IReadOnlyList<string> ProcessingStatuses =
        ["generating_image", "generating_mockup", "generating_video", "generating_listing"];

    /// <summary>The column a status belongs to; null for failed, skipped and cancelled products.</summary>
    public static string? StageOf(string processingStatus) => processingStatus switch
    {
        "pending" or "validating" or "queued" => Queued,
        "generating_image" => Designing,
        "image_review_required" => DesignReview,
        "generating_mockup" => Mockup,
        "generating_video" => Video,
        "generating_listing" or "review_required" => Listing,
        "approved" or "exported" => Ready,
        "published" => Published,
        _ => null,
    };
}
