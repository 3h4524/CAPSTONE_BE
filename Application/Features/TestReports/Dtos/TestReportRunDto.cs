namespace APCS.Application.Features.TestReports.Dtos;

/// <summary>
/// One test run: when it ran, what it covered and how it ended.
/// </summary>
public sealed record TestReportRunDto
{
    /// <summary>
    /// Gets the identifier of the run, unique per run.
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Gets the moment the run started.
    /// </summary>
    public required DateTime StartedAtUtc { get; init; }

    /// <summary>
    /// Gets the moment the run finished, or null while it is still going.
    /// </summary>
    public DateTime? FinishedAtUtc { get; init; }

    /// <summary>
    /// Gets how long the run took, in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Gets the state of the run: running, passed, failed or error.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Gets the branch the run was made on.
    /// </summary>
    public string? Branch { get; init; }

    /// <summary>
    /// Gets the commit the run was made on.
    /// </summary>
    public string? CommitSha { get; init; }

    /// <summary>
    /// Gets the admin who started the run.
    /// </summary>
    public string? TriggeredBy { get; init; }

    /// <summary>
    /// Gets how many tests the run covered.
    /// </summary>
    public int TotalTests { get; init; }

    /// <summary>
    /// Gets how many tests passed.
    /// </summary>
    public int PassedTests { get; init; }

    /// <summary>
    /// Gets how many tests failed.
    /// </summary>
    public int FailedTests { get; init; }

    /// <summary>
    /// Gets how many tests passed only after a retry.
    /// </summary>
    public int FlakyTests { get; init; }

    /// <summary>
    /// Gets how many tests were skipped.
    /// </summary>
    public int SkippedTests { get; init; }

    /// <summary>
    /// Gets how many of the failed tests pin a known gap instead of a new problem.
    /// </summary>
    public int KnownIssueTests { get; init; }

    /// <summary>
    /// Gets how many of the failed tests are new problems that need a look.
    /// </summary>
    public int UnexpectedFailedTests { get; init; }

    /// <summary>
    /// Gets how many spec files the run covered.
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// Gets how many suites the run covered.
    /// </summary>
    public int SuiteCount { get; init; }

    /// <summary>
    /// Gets the reason the run could not produce a report.
    /// </summary>
    public string? FailureSummary { get; init; }
}
