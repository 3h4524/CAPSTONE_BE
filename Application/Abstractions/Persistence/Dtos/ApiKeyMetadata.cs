namespace APCS.Application.Abstractions.Persistence.Dtos;

/// <summary>Non-secret metadata from non-deleted credentials owned by one seller.</summary>
public sealed record ApiKeyMetadata(
    Guid Id, string Provider, string Label, string? Last4, string AuthType,
    string? ConnectedAccountName, string? Environment, bool? IsActive,
    DateTime? ExpiresAt, DateTime? LastUsedAt, DateTime? LastCheckedAt, bool? LastCheckSucceeded);
