namespace APCS.Common.Constants;

/// <summary>
/// The lifecycle values stored in <c>BatchJobProduct.Status</c> and mirrored on
/// <c>Product.ProcessingStatus</c>. Must stay within the DB CHECK constraints
/// <c>chk_batch_job_products_status</c> / <c>chk_products_status</c>.
/// </summary>
public static class BatchJobProductStatuses
{
    public const string Pending = "pending";
    public const string Queued = "queued";

    /// <summary>The AI provider is being called for this product.</summary>
    public const string GeneratingImage = "generating_image";

    /// <summary>Images were generated and now wait for the Seller to approve/reject them.</summary>
    public const string ImageReviewRequired = "image_review_required";

    public const string Failed = "failed";
}
