using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;

namespace APCS.Infrastructure.Services;

/// <summary>
/// Uses a platform MiniMax key (one an Admin keeps on the admin page) while preserving per-owner credentials for legacy
/// routes.
/// </summary>
public sealed class AiVideoCredentialResolver(
    IApiKeyRepository keys,
    IApiKeyCredentials credentials,
    ISystemApiKeyPool? systemKeys = null) : IAiVideoCredentialResolver
{
    public async Task<string?> ResolveAsync(Guid ownerId, string providerKey, CancellationToken cancellationToken)
    {
        if (string.Equals(providerKey, AiVideoModel.ProviderKey, StringComparison.OrdinalIgnoreCase))
            return Normalize(systemKeys?.AnyKey(AiVideoModel.ProviderKey));

        var key = (await keys.ListOwnedAsync(ownerId, cancellationToken)).FirstOrDefault(candidate =>
            string.Equals(candidate.ServiceProvider.Trim(), providerKey, StringComparison.OrdinalIgnoreCase) &&
            candidate.IsActive == true && candidate.LastCheckSucceeded == true);
        return key is null ? null : Normalize(credentials.Unprotect(key.KeyValueEncrypted));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
