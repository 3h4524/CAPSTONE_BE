namespace APCS.Application.Abstractions;

/// <summary>
/// What a test run produced.
/// </summary>
/// <param name="ExitCode">The exit code of the test command.</param>
/// <param name="Log">Everything the test command printed.</param>
/// <param name="RawReport">The report Playwright wrote, or null when it wrote none.</param>
/// <param name="Branch">The branch the frontend was on.</param>
/// <param name="CommitSha">The commit the frontend was on.</param>
public sealed record TestReportRunnerResult(
    int ExitCode,
    string Log,
    string? RawReport,
    string? Branch,
    string? CommitSha);

/// <summary>
/// Runs the test suite and brings back what it wrote.
/// </summary>
public interface ITestReportRunner
{
    /// <summary>
    /// Runs the test suite once.
    /// </summary>
    Task<TestReportRunnerResult> RunAsync(string runId, CancellationToken cancellationToken = default);
}
