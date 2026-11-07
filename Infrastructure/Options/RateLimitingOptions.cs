using System.ComponentModel.DataAnnotations;

namespace APCS.Infrastructure.Options;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    [Range(1, 100_000)]
    public int UserPermitsPerMinute { get; set; } = 600;

    [Range(1, 100_000)]
    public int AnonymousPermitsPerMinute { get; set; } = 300;

    [Range(1, 1_000)]
    public int AuthPermitsPerMinute { get; set; } = 20;

    [Range(1, 1_000)]
    public int OtpPermitsPerMinute { get; set; } = 5;
}
