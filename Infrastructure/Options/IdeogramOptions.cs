using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>Platform-level defaults for the Ideogram image generation calls. The API key itself is
/// never here — it comes from the Seller's own BYOK <c>ApiKey</c> row.</summary>
public sealed class IdeogramOptions
{
    public const string SectionName = ConfigurationSections.Ideogram;

    public string Endpoint { get; set; } = "https://api.ideogram.ai/v2/image/generate/";

    /// <summary>Where a generation accepted to run in the background is polled, by its id.</summary>
    public string GenerationsEndpoint { get; set; } = "https://api.ideogram.ai/v2/generations/";

    /// <summary>The model endpoint for an image with a backdrop; "-transparent" is appended for a transparent one.</summary>
    public string Model { get; set; } = "ideogram-4";

    /// <summary>"turbo", "default" or "quality": the detail Ideogram renders, and what it charges.</summary>
    public string RenderingSpeed { get; set; } = "turbo";

    /// <summary>
    /// "auto", "on" or "off": whether Ideogram may rewrite the prompt with more detail first. Off keeps the wording the
    /// prompt rules chose (Ideogram only restructures it). A product with next to nothing to go on is always sent "on".
    /// </summary>
    public string MagicPrompt { get; set; } = "off";

    /// <summary>"2k" (2048 px on the long side of a square, for print) or "1k" (1024 px: a quarter of the pixels, so
    /// smaller files that download and upload faster).</summary>
    public string OutputResolution { get; set; } = "2k";

    /// <summary>
    /// Ideogram's price for one image when it does not report the amount it charged. Left empty, it follows
    /// <see cref="RenderingSpeed"/> (turbo $0.03, default $0.06, quality $0.10).
    /// </summary>
    public decimal? EstimatedCostPerImageUsd { get; set; }

    /// <summary>The price recorded for one image when Ideogram does not report it, as <c>ApiUsageRecord.CostUsd</c>.</summary>
    public decimal CostPerImageUsd => EstimatedCostPerImageUsd ?? RenderingSpeed.Trim().ToLowerInvariant() switch
    {
        "turbo" => 0.03m,
        "quality" => 0.10m,
        _ => 0.06m
    };

    /// <summary>Where an image is edited with reference images (the lifestyle image); the edit model is appended.</summary>
    public string EditEndpoint { get; set; } = "https://api.ideogram.ai/v2/image/precise-edit/";

    /// <summary>The model that edits an image with reference images.</summary>
    public string EditModel { get; set; } = "ideogram-4-5";

    /// <summary>
    /// "very_low", "low", "medium" or "high": the quality of an edit, and what it costs ($0.008, $0.03, $0.06, $0.22).
    /// Low costs half of medium; medium keeps the cut of the garment on a lifestyle image a little closer to its mock-up.
    /// </summary>
    public string EditQuality { get; set; } = "low";

    /// <summary>The price recorded for one edit when Ideogram does not report what it charged.</summary>
    public decimal EditCostPerImageUsd => EditQuality.Trim().ToLowerInvariant() switch
    {
        "very_low" => 0.008m,
        "low" => 0.03m,
        "high" => 0.22m,
        _ => 0.06m
    };

    /// <summary>
    /// How many images of one job are requested from Ideogram at the same time. Ideogram allows 10 in flight per
    /// account; two are left for anything else running on the same key (a request over the limit is retried after a
    /// pause, so an overlap only slows things down). Images also download and upload alongside, which a slow
    /// connection may not keep up with; lower it there.
    /// </summary>
    public int MaxConcurrentImages { get; set; } = 8;

    /// <summary>How often an image being made is asked about.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How many times in a row asking about an image may fail (Ideogram busy or unreachable) before giving up on it.</summary>
    public int MaxPollFailures { get; set; } = 5;

    /// <summary>How many times a finished image is downloaded before giving up; each new try asks Ideogram for a fresh link.</summary>
    public int DownloadAttempts { get; set; } = 3;

    /// <summary>
    /// Has Ideogram check each finished image for copyrighted content (a real brand's logo, a known character) and refuse
    /// the ones it flags, which a design for sale must not carry. It costs nothing more; it adds a little time.
    /// </summary>
    public bool CopyrightDetection { get; set; } = true;

    /// <summary>How many times a call Ideogram turned away as busy (429 or 503) is sent again.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>The wait before the first repeat of a busy call; it doubles for each further one.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
}
