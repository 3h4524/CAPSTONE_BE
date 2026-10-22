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
    /// <param name="runId">The run the suite belongs to.</param>
    /// <param name="environment">
    /// The variables the suite reads, such as the seller credentials. They go into the environment
    /// of the child process, never into the command line, so they do not show up in the process
    /// list.
    /// </param>
    /// <param name="cancellationToken">Stops the run.</param>
    Task<TestReportRunnerResult> RunAsync(
        string runId,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default);
}
