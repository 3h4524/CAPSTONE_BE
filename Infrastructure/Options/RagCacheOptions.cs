using System.ComponentModel.DataAnnotations;

namespace APCS.Infrastructure.Options;

public sealed class RagCacheOptions
{
    public const string SectionName = "Rag:Cache";

    public bool Enabled { get; set; } = true;

    [Range(1, 720)]
    public int EmbeddingTtlHours { get; set; } = 24;

    [Range(1, 1440)]
    public int SearchTtlMinutes { get; set; } = 10;
}
