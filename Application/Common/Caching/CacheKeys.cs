namespace APCS.Application.Common.Caching;

/// <summary>
/// Provides well-known cache key patterns for user-scoped data.
/// Always include userId in user-owned cache keys for isolation.
/// </summary>
public static class CacheKeys
{
    /// <summary>
    /// Cache key for a user's profile.
    /// </summary>
    public static string UserProfile(Guid userId)
        => $"users:{userId}:profile";

    /// <summary>
    /// Cache key for a user's API key list.
    /// </summary>
    public static string ApiKeys(Guid userId)
        => $"users:{userId}:api-keys";

    /// <summary>
    /// Cache key for a batch preview.
    /// </summary>
    public static string BatchPreview(Guid userId, Guid batchId)
        => $"users:{userId}:batches:{batchId}:preview";

    /// <summary>
    /// Cache key for a product queue.
    /// </summary>
    public static string ProductQueue(Guid userId, Guid batchId)
        => $"users:{userId}:batches:{batchId}:products";
}
