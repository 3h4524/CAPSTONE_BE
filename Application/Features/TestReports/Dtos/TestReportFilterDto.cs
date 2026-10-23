namespace APCS.Application.Features.TestReports.Dtos;

/// <summary>
/// How to narrow the list of test runs.
/// </summary>
public sealed class TestReportFilterDto
{
    /// <summary>
    /// Gets or sets the earliest run to include.
    /// </summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>
    /// Gets or sets the latest run to include.
    /// </summary>
    public DateTime? ToUtc { get; set; }

    /// <summary>
    /// Gets or sets the state to keep: running, passed, failed or error.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the page to return, one-based.
    /// </summary>
    public int PageIndex { get; set; } = 1;

    /// <summary>
    /// Gets or sets how many runs a page holds.
    /// </summary>
    public int PageSize { get; set; } = 20;
}
