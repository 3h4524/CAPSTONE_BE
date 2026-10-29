namespace APCS.Application.Features.DesignGeneration.Common;

/// <summary>How a design generation job is run.</summary>
/// <remarks>
/// A narrow Application-owned projection of the providers' options (Infrastructure), so Application
/// never references the Infrastructure options types directly.
/// </remarks>
/// <param name="MaxConcurrentImages">
/// How many images of one job are requested from the provider at the same time. The calls use the
/// Seller's own key, so this has to stay within that key's rate limit; 1 makes one image at a time.
/// </param>
public sealed record DesignGenerationSettings(int MaxConcurrentImages)
{
    /// <summary>A provider's own limit where it differs from <see cref="MaxConcurrentImages"/>, by provider name.</summary>
    public IReadOnlyDictionary<string, int> MaxConcurrentImagesByProvider { get; init; } = new Dictionary<string, int>();

    /// <summary>The most one image may take at the provider: the request, any waiting for it, and its download.</summary>
    public TimeSpan ProviderTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The most one image may take to upload once it is made.</summary>
    public TimeSpan UploadTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How many images of one job the named provider is asked for at the same time.</summary>
    public int MaxConcurrentImagesFor(string provider) =>
        Math.Max(1, MaxConcurrentImagesByProvider.TryGetValue(provider, out var limit) ? limit : MaxConcurrentImages);
}
