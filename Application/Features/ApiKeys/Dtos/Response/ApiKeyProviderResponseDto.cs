namespace APCS.Application.Features.ApiKeys.Dtos.Response;

public sealed record ApiKeyProviderResponseDto(string Id, string Name, bool Available, IReadOnlyList<string> Permissions);
