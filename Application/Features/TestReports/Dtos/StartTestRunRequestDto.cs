namespace APCS.Application.Features.TestReports.Dtos;

public sealed record StartTestRunRequestDto
{
    public string? Mode { get; init; }
}
