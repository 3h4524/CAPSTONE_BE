using System.Text.Json;

namespace APCS.Application.Features.TestReports.Dtos;

public sealed record TestReportIngestDto
{
    public string? RunId { get; init; }

    public string? Branch { get; init; }

    public string? CommitSha { get; init; }

    public string? TriggeredBy { get; init; }

    public JsonElement Report { get; init; }

    public string? FailureSummary { get; init; }
}
