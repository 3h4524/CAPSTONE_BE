using System.ComponentModel.DataAnnotations;
using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>
/// Provides strongly typed Redis configuration.
/// </summary>
public sealed class RedisOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = ConfigurationSections.Redis;

    /// <summary>
    /// Gets or sets the Redis connection string.
    /// </summary>
    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the cache key instance prefix.
    /// </summary>
    public string InstanceName { get; init; } = "APCS:";

    /// <summary>
    /// Gets or sets the default cache expiration in minutes.
    /// </summary>
    [Range(1, 1440)]
    public int DefaultExpirationMinutes { get; init; } = 10;
}
