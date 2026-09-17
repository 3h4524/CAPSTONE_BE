namespace APCS.Application.Features.ApiKeys.Dtos.Request;

public sealed class SaveApiKeyRequestDto
{
    public string Provider { get; init; } = "";
    public string? Name { get; init; }
    public string? Environment { get; init; }
    public string? ApiKey { get; init; }
    public bool Confirmed { get; init; }
}
