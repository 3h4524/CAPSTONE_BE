namespace APCS.Application.Abstractions.AI;

/// <summary>Resolves the credential captured by an AI-video provider route.</summary>
public interface IAiVideoCredentialResolver
{
    /// <summary>
    /// Returns the system MiniMax credential for the default route, or the owner's credential for
    /// a legacy provider route. A missing credential is returned as <see langword="null"/>.
    /// </summary>
    Task<string?> ResolveAsync(Guid ownerId, string providerKey, CancellationToken cancellationToken);
}
