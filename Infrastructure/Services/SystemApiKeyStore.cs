using System.Security.Cryptography;
using APCS.Application.Abstractions.AI;
using APCS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace APCS.Infrastructure.Services;

/// <summary>One provider's platform keys and limits, as <see cref="SystemApiKeyPool"/> uses them.</summary>
/// <param name="Provider">The provider, lower case.</param>
/// <param name="DailyLimitPerKey">How many requests each key may make in a day (UTC); null for no limit.</param>
/// <param name="MaxConcurrentPerKey">How many requests each key may have running at once on a server; null for no limit.</param>
/// <param name="Strategy"><see cref="SystemApiKeyStrategies.Fill"/> or <see cref="SystemApiKeyStrategies.Rotate"/>.</param>
/// <param name="Keys">The keys, in the order fill takes them.</param>
public sealed record SystemApiKeyProviderConfig(
    string Provider,
    int? DailyLimitPerKey,
    int? MaxConcurrentPerKey,
    string? Strategy,
    IReadOnlyList<SystemApiKeyConfig> Keys);

/// <summary>One platform key, decrypted.</summary>
/// <param name="Value">The key itself.</param>
/// <param name="IsActive">
/// False for a key an Admin switched off: no new request takes it, but a video it started can still be asked about.
/// </param>
public sealed record SystemApiKeyConfig(string Value, bool IsActive);

/// <summary>Where <see cref="SystemApiKeyPool"/> reads the keys from.</summary>
public interface ISystemApiKeySource
{
    /// <summary>Every provider's limits and keys, not deleted.</summary>
    Task<IReadOnlyList<SystemApiKeyProviderConfig>> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the keys an Admin keeps on the admin page, decrypted, on a context of its own. A key this server cannot decrypt
/// is left out (and named in the log by its id only); a database the migration has not reached yet has no keys.
/// </summary>
public sealed class SystemApiKeyStore(IServiceScopeFactory scopes, ISystemApiKeySecrets secrets, ILogger<SystemApiKeyStore> logger)
    : ISystemApiKeySource
{
    private const string UndefinedTable = "42P01";

    public async Task<IReadOnlyList<SystemApiKeyProviderConfig>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            var providers = await db.SystemApiKeyProviders.AsNoTracking().ToListAsync(cancellationToken);
            var keys = await db.SystemApiKeys.AsNoTracking()
                .Where(key => key.DeletedAt == null)
                .OrderBy(key => key.SortOrder).ThenBy(key => key.CreatedAt)
                .Select(key => new { key.Provider, key.KeyId, key.Label, key.KeyValueEncrypted, key.IsActive })
                .ToListAsync(cancellationToken);

            return providers.Select(provider => new SystemApiKeyProviderConfig(
                    provider.Provider,
                    provider.DailyLimitPerKey,
                    provider.MaxConcurrentPerKey,
                    provider.Strategy,
                    keys.Where(key => key.Provider == provider.Provider)
                        .Select(key => Decrypt(key.Provider, key.KeyId, key.Label, key.KeyValueEncrypted, key.IsActive))
                        .OfType<SystemApiKeyConfig>()
                        .ToList()))
                .ToList();
        }
        catch (PostgresException error) when (error.SqlState == UndefinedTable)
        {
            logger.LogWarning("The system API key tables do not exist yet; the platform has no keys until the migration runs");
            return [];
        }
    }

    private SystemApiKeyConfig? Decrypt(string provider, string keyId, string label, string encrypted, bool isActive)
    {
        try
        {
            var value = secrets.Unprotect(encrypted);
            return string.IsNullOrWhiteSpace(value) ? null : new SystemApiKeyConfig(value.Trim(), isActive);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("System {Provider} key {KeyId} ({Label}) cannot be decrypted on this server and is left out",
                provider, keyId, label);
            return null;
        }
    }
}
