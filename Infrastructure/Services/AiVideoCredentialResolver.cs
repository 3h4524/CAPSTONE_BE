using APCS.Application.Abstractions.AI;
using APCS.Application.Abstractions.Authentication;
using APCS.Application.Abstractions.Persistence;
using APCS.Application.Features.Workflows;
using APCS.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace APCS.Infrastructure.Services;

/// <summary>Uses the platform MiniMax key while preserving per-owner credentials for legacy routes.</summary>
public sealed class AiVideoCredentialResolver(
    IOptions<MiniMaxOptions> miniMax,
    IApiKeyRepository keys,
    IApiKeyCredentials credentials) : IAiVideoCredentialResolver
{
    public async Task<string?> ResolveAsync(Guid ownerId, string providerKey, CancellationToken cancellationToken)
    {
        if (string.Equals(providerKey, AiVideoModel.ProviderKey, StringComparison.OrdinalIgnoreCase))
            return Normalize(miniMax.Value.ApiKey);

        var key = (await keys.ListOwnedAsync(ownerId, cancellationToken)).FirstOrDefault(candidate =>
            string.Equals(candidate.ServiceProvider.Trim(), providerKey, StringComparison.OrdinalIgnoreCase) &&
            candidate.IsActive == true && candidate.LastCheckSucceeded == true);
        return key is null ? null : Normalize(credentials.Unprotect(key.KeyValueEncrypted));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
