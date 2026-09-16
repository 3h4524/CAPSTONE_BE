namespace APCS.Application.Features.ApiKeys.Dtos.Response;

public sealed record ListApiKeysResponseDto(
    IReadOnlyList<ApiKeyResponseDto> Items,
    int ConnectedCount,
    int NeedsAttentionCount,
    bool IsReady,
    DateTimeOffset? LastCheckedAtUtc);

public sealed record ApiKeyResponseDto(
    Guid Id,
    string Provider,
    string ProviderCategory,
    string AuthType,
    string Label,
    string Credential,
    string? Environment,
    string Status,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? LastCheckedAtUtc);