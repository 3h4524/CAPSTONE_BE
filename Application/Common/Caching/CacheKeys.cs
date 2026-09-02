namespace APCS.Application.Common.Caching;

/// <summary>
/// Provides well-known cache key patterns for seller-scoped data.
/// Always includes the seller identifier to preserve tenant isolation.
/// </summary>
public static class CacheKeys
{
    /// <summary>
    /// Cache key for a seller's profile.
    /// </summary>
    public static string SellerProfile(int sellerId)
        => $"sellers:{sellerId}:profile";

    /// <summary>
    /// Cache key for a seller's API key list.
    /// </summary>
    public static string ApiKeys(int sellerId)
        => $"sellers:{sellerId}:api-keys";

    /// <summary>
    /// Cache key for a batch preview.
    /// </summary>
    public static string BatchPreview(int sellerId, int batchId)
        => $"sellers:{sellerId}:batches:{batchId}:preview";

    /// <summary>
    /// Cache key for a product queue.
    /// </summary>
    public static string ProductQueue(int sellerId, int batchId)
        => $"sellers:{sellerId}:batches:{batchId}:products";
}
