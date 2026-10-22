namespace APCS.Application.Features.TestReports.Dtos;

/// <summary>
/// One run with the tests it covered.
/// </summary>
public sealed record TestReportDetailDto
{
    /// <summary>
    /// Gets the run itself.
    /// </summary>
    public required TestReportRunDto Run { get; init; }

    /// <summary>
    /// Gets the spec files the run covered, grouped per file and project.
    /// </summary>
    public IReadOnlyList<TestReportFileDto> Files { get; init; } = [];
}
