using APCS.Common.Constants;
using Microsoft.Extensions.Configuration;

namespace APCS.Infrastructure.Options;

/// <summary>
/// The platform's own AI provider keys, by provider (<c>"ideogram"</c>, <c>"minimax"</c>), read from the environment:
/// <code>
/// SystemApiKeys__Ideogram__Keys=first-key;second-key
/// SystemApiKeys__Ideogram__DailyLimitPerKey=500
/// SystemApiKeys__Ideogram__MaxConcurrentPerKey=8
/// SystemApiKeys__Ideogram__Strategy=fill
/// SystemApiKeys__MiniMax__Keys=...
/// SystemApiKeys__MiniMax__DailyLimitPerKey=100
/// </code>
/// The single keys configured before there was a pool count too: <c>IDEOGRAM_API_KEY</c> and <c>MiniMax__ApiKey</c>.
/// </summary>
public sealed class SystemApiKeysOptions
{
    public const string SectionName = ConfigurationSections.SystemApiKeys;

    /// <summary>The single Ideogram key of the environment, used before there was a pool.</summary>
    public const string LegacyIdeogramKey = "IDEOGRAM_API_KEY";

    /// <summary>
    /// Ideogram allows 10 images in flight per account; two are left for anything else running on the same account, as
    /// <see cref="IdeogramOptions.MaxConcurrentImages"/> does.
    /// </summary>
    public const int DefaultIdeogramConcurrency = 8;

    /// <summary>The keys and limits of each provider, by its lower-case name.</summary>
    public Dictionary<string, SystemApiKeyProviderOptions> Providers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the pool from the configuration, together with the single keys configured before it.</summary>
    public static void Read(IConfiguration configuration, SystemApiKeysOptions options)
    {
        foreach (var section in configuration.GetSection(SectionName).GetChildren())
        {
            var provider = options.For(section.Key);
            provider.Keys.AddRange(Split(section["Keys"]));
            if (int.TryParse(section["DailyLimitPerKey"], out var dailyLimit) && dailyLimit >= 0)
                provider.DailyLimitPerKey = dailyLimit;
            if (int.TryParse(section["MaxConcurrentPerKey"], out var concurrency) && concurrency > 0)
                provider.MaxConcurrentPerKey = concurrency;
            if (!string.IsNullOrWhiteSpace(section["Strategy"]))
                provider.Strategy = section["Strategy"]!.Trim();
        }

        options.For("ideogram").Keys.AddRange(Split(configuration[LegacyIdeogramKey]));
        options.For("minimax").Keys.AddRange(Split(configuration[$"{ConfigurationSections.MiniMax}:ApiKey"]));

        foreach (var (name, provider) in options.Providers)
        {
            var distinct = provider.Keys.Distinct(StringComparer.Ordinal).ToList();
            provider.Keys.Clear();
            provider.Keys.AddRange(distinct);
            if (provider.MaxConcurrentPerKey is null && string.Equals(name, "ideogram", StringComparison.OrdinalIgnoreCase))
                provider.MaxConcurrentPerKey = DefaultIdeogramConcurrency;
        }
    }

    private SystemApiKeyProviderOptions For(string provider)
    {
        if (!Providers.TryGetValue(provider, out var options))
            Providers[provider] = options = new SystemApiKeyProviderOptions();
        return options;
    }

    // Several keys in one value, split on ';' as Cors__AllowedOrigins is (or ',' or a new line).
    private static IEnumerable<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>The platform keys of one provider and how far each one may be used.</summary>
public sealed class SystemApiKeyProviderOptions
{
    /// <summary>The keys, in the order <see cref="Strategy"/> takes them.</summary>
    public List<string> Keys { get; } = [];

    /// <summary>How many requests each key may make in a day (UTC); null for no limit.</summary>
    public int? DailyLimitPerKey { get; set; }

    /// <summary>How many requests each key may have running at the same time; null for no limit.</summary>
    public int? MaxConcurrentPerKey { get; set; }

    /// <summary>
    /// How the keys are taken: <see cref="SystemApiKeyStrategies.Fill"/> (the default) or
    /// <see cref="SystemApiKeyStrategies.Rotate"/>; null for the default.
    /// </summary>
    public string? Strategy { get; set; }
}

/// <summary>How a provider's platform keys are taken.</summary>
public static class SystemApiKeyStrategies
{
    /// <summary>
    /// The first key until it is used up or refused for the day, then the next one. A request finding every place of a key
    /// taken goes on to the next key rather than waiting, so a busy hour still runs on every key.
    /// </summary>
    public const string Fill = "fill";

    /// <summary>Each request starts at the key after the one the last request started at, so the uses spread evenly.</summary>
    public const string Rotate = "rotate";
}
