namespace APCS.Application.Features.DesignGeneration.Common;

/// <summary>How a design generation job is run.</summary>
/// <remarks>
/// A narrow Application-owned projection of <c>GeminiOptions</c> (Infrastructure), so Application
/// never references the Infrastructure options type directly.
/// </remarks>
/// <param name="MaxConcurrentImages">
/// How many images of one job are requested from the provider at the same time. The calls use the
/// Seller's own key, so this has to stay within that key's rate limit; 1 makes one image at a time.
/// </param>
public sealed record DesignGenerationSettings(int MaxConcurrentImages);
