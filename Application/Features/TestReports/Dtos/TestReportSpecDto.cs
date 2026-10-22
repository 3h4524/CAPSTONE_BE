namespace APCS.Application.Features.TestReports.Dtos;

/// <summary>
/// One test, with what went wrong when it did not pass.
/// </summary>
public sealed record TestReportSpecDto
{
    /// <summary>
    /// Gets the full name of the test: its describe blocks and its own title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the state of the test: passed, failed, flaky or skipped.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Gets how long the test took, in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Gets how many times the test was retried.
    /// </summary>
    public int Retries { get; init; }

    /// <summary>
    /// Gets whether the test pins a gap that is known, which the team names "BUG B".
    /// Such a test fails while the gap is there and stops failing once it is fixed.
    /// </summary>
    public bool IsKnownIssue { get; init; }

    /// <summary>
    /// Gets the message of the failure.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the stack trace of the failure.
    /// </summary>
    public string? ErrorStack { get; init; }

    /// <summary>
    /// Gets the lines of the spec file around the failure.
    /// </summary>
    public string? ErrorSnippet { get; init; }
}
