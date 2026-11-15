namespace APCS.Application.Abstractions.AI;

/// <summary>Making images on the platform's keys, the same way for every feature that makes them.</summary>
public static class SystemApiKeyPoolExtensions
{
    /// <summary>
    /// Makes one image on the platform's keys in turn. Every request made on a platform key counts toward its daily
    /// limit, except one the provider turned the account away for (<see cref="ImageGenerationResult.StopsJob"/>): that
    /// key is passed over for the rest of the day and the image is asked of the next one. Once every platform key is used
    /// up, the Seller's own key (<paramref name="ownKey"/>) takes over when they have one; without one the result is an
    /// account refusal saying the platform has made as many images as it allows today.
    /// A key that still has too many images at once after the provider's own waits (<see cref="ImageGenerationResult.KeyBusy"/>)
    /// gets its use back and stays in use, and the image is asked of another key. When no other key can take it, the
    /// image fails as busy: the Seller's own key is not charged for the platform's keys being busy.
    /// </summary>
    public static async Task<ImageGenerationResult> GenerateImageAsync(this ISystemApiKeyPool pool, string provider, string? ownKey,
        Func<string, Task<ImageGenerationResult>> generate, CancellationToken cancellationToken = default)
    {
        var busyKeys = new HashSet<string>();
        ImageGenerationResult? busy = null;
        while (true)
        {
            await using var lease = await pool.AcquireAsync(provider, busyKeys, cancellationToken);
            if (lease is null)
            {
                if (busy is not null) return busy;
                return ownKey is not null
                    ? await generate(ownKey)
                    : ImageGenerationResult.AccountRefused(UsedUpMessage(provider), provider);
            }

            var result = await generate(lease.ApiKey);
            if (result.KeyBusy)
            {
                await lease.RefundAsync(CancellationToken.None);
                busyKeys.Add(lease.KeyId);
                busy = result;
                continue;
            }
            if (!result.StopsJob) return result;
            await lease.DisableForTodayAsync(result.ErrorMessage ?? "The provider refused the account.", CancellationToken.None);
        }
    }

    /// <summary>What a Seller is told when the platform's keys for the provider are used up for today.</summary>
    public static string UsedUpMessage(string provider) =>
        $"The platform has made as many {ImageGenerationProviders.Label(provider)} images as it allows today. "
        + "Try again tomorrow, or connect your own key in API Keys.";
}
