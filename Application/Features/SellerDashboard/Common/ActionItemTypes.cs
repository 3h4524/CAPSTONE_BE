namespace APCS.Application.Features.SellerDashboard.Common;

/// <summary>The kinds of work the Dashboard's action inbox reports.</summary>
public static class ActionItemTypes
{
    public const string EtsyReconnect = "etsy_reconnect";
    public const string EtsyUploadFailed = "etsy_upload_failed";
    public const string BatchFailed = "batch_failed";
    public const string VideoFailed = "video_failed";
    public const string StuckProducts = "stuck_products";
    public const string DesignReview = "design_review";
    public const string ListingReview = "listing_review";
    public const string VideoReview = "video_review";
    public const string LowSeo = "low_seo";
}

/// <summary>The severities an action item can carry, matching <c>notification_alerts.severity</c>.</summary>
public static class ActionItemSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";
}
