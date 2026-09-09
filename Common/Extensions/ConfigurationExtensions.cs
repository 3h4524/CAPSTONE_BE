using Microsoft.Extensions.Configuration;

namespace APCS.Common.Extensions;

/// <summary>
/// Provides safe configuration accessors that work with appsettings and environment variables.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Gets a required configuration value from appsettings, user secrets, or environment variables.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="key">The configuration key using colon-separated section syntax.</param>
    /// <returns>The configured value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the value is missing.</exception>
    public static string GetRequiredValue(this IConfiguration configuration, string key)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var value = configuration[key]
            ?? Environment.GetEnvironmentVariable(key)
            ?? Environment.GetEnvironmentVariable(ToEnvironmentKey(key));

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' is not configured.");
        }

        return value;
    }

    /// <summary>
    /// Gets an optional string array configuration value.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="key">The configuration key using colon-separated section syntax.</param>
    /// <returns>The configured values, or an empty array when missing.</returns>
    public static string[] GetOptionalStringArray(this IConfiguration configuration, string key)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var values = configuration.GetSection(key).Get<string[]>();
        if (values is { Length: > 0 })
        {
            return values;
        }

        var environmentValue = Environment.GetEnvironmentVariable(key)
            ?? Environment.GetEnvironmentVariable(ToEnvironmentKey(key));

        return string.IsNullOrWhiteSpace(environmentValue)
            ? Array.Empty<string>()
            : environmentValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Gets a required connection string from appsettings or environment variables.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="name">The connection string name.</param>
    /// <returns>The configured connection string.</returns>
    public static string GetRequiredConnectionStringValue(this IConfiguration configuration, string name)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var key = $"ConnectionStrings:{name}";
        var value = configuration.GetConnectionString(name)
            ?? Environment.GetEnvironmentVariable(key)
            ?? Environment.GetEnvironmentVariable(ToEnvironmentKey(key));

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Connection string '{name}' is not configured.");
        }

        return value;
    }

    /// <summary>
    /// Gets a required configuration section.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The section name.</param>
    /// <returns>The existing configuration section.</returns>
    public static IConfigurationSection GetRequiredConfigurationSection(
        this IConfiguration configuration,
        string sectionName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
        {
            throw new InvalidOperationException($"Configuration section '{sectionName}' is not configured.");
        }

        return section;
    }

    /// <summary>
    /// Binds a required configuration section to a strongly typed object.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="sectionName">The section name.</param>
    /// <returns>The bound options instance.</returns>
    public static TOptions GetRequiredOptions<TOptions>(
        this IConfiguration configuration,
        string sectionName)
        where TOptions : class, new()
    {
        var section = configuration.GetRequiredConfigurationSection(sectionName);
        var options = section.Get<TOptions>();

        return options ?? throw new InvalidOperationException(
            $"Configuration section '{sectionName}' could not be bound to {typeof(TOptions).Name}.");
    }

    /// <summary>
    /// Converts a colon-separated configuration key to an environment variable key.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <returns>The equivalent environment variable key.</returns>
    public static string ToEnvironmentKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Replace(":", "__", StringComparison.Ordinal);
    }
}
